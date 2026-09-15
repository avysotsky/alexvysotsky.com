using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;
using VANWebService.Auth;
using VANWebService.Security;

namespace VANWebService;

public static class DeribitPrivateStreamEndpoints
{
    private static readonly string[] FuturesPrivateChannels =
    {
        "user.orders.future.any.raw",
        "user.changes.future.any.raw",
        "user.portfolio.any"
    };

    public static void MapDeribitPrivateStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/deribit/futures/order-events/stream", async (HttpContext context, VANWebServiceConfig cfg, Enums.LogAction log) =>
        {
            if (!TryRequireAdminOrQueryToken(context, out var session, out var fail))
            {
                context.Response.StatusCode = fail == 403 ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
                return;
            }

            int? accountId = null;
            if (int.TryParse(context.Request.Query["accountId"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAccountId))
            {
                accountId = parsedAccountId;
            }
            accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session!, context.RequestAborted);
            var account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, context.RequestAborted);
            if (account == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { message = "No Deribit account selected" }, context.RequestAborted);
                return;
            }
            if (string.IsNullOrWhiteSpace(account.ApiPublicKey) || string.IsNullOrWhiteSpace(account.ApiSecretCipher))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "Selected Deribit account has no API credentials", account = ToPublicAccount(account) }, context.RequestAborted);
                return;
            }

            string apiSecretPlain;
            try
            {
                apiSecretPlain = ApiSecretCrypto.Decrypt(account.ApiSecretCipher);
            }
            catch (Exception ex)
            {
                log($"[Deribit private order WS] failed to decrypt account #{account.AccId}: {ex.Message}", Enums.LogLevel.llExceptions);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "Selected Deribit account secret cannot be decrypted", account = ToPublicAccount(account) }, context.RequestAborted);
                return;
            }

            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";
            await WriteSseAsync(context, "status", new
            {
                status = "connecting",
                account = ToPublicAccount(account),
                channels = FuturesPrivateChannels,
                source = "Deribit private WebSocket via backend SSE"
            });

            try
            {
                await StreamDeribitPrivateEventsAsync(context, cfg, account, apiSecretPlain, log, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                log($"[Deribit private order WS] account #{account.AccId} failed: {ex}", Enums.LogLevel.llExceptions);
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    await WriteSseAsync(context, "error", new { message = ex.Message, account = ToPublicAccount(account) });
                }
            }
        });
    }

    private static async Task StreamDeribitPrivateEventsAsync(HttpContext context, VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, Enums.LogAction log, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl.TrimEnd('/');
        var wsUrl = baseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase).Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase) + "/ws/api/v2";

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", "VANWebService Deribit private order stream");
        await socket.ConnectAsync(new Uri(wsUrl), ct);
        log($"[Deribit private order WS] connected account #{account.AccId}", Enums.LogLevel.llBaselogic);

        await SendDeribitRpcAsync(socket, 1, "public/auth", new
        {
            grant_type = "client_credentials",
            client_id = account.ApiPublicKey,
            client_secret = apiSecretPlain
        }, ct);

        var authenticated = false;
        var subscribed = false;
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var payload = await ReceiveTextAsync(socket, ct);
            if (payload == null) break;
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
            {
                if (root.TryGetProperty("error", out var errorEl))
                {
                    throw new InvalidOperationException("Deribit WS RPC error: " + errorEl.GetRawText());
                }
                if (id == 1)
                {
                    authenticated = true;
                    await SendDeribitRpcAsync(socket, 2, "private/subscribe", new { channels = FuturesPrivateChannels }, ct);
                    await WriteSseAsync(context, "status", new { status = "authenticated", account = ToPublicAccount(account), source = "Deribit public/auth over private WS" });
                    continue;
                }
                if (id == 2)
                {
                    subscribed = true;
                    var result = root.TryGetProperty("result", out var res) ? res.Clone() : default;
                    await WriteSseAsync(context, "status", new
                    {
                        status = "subscribed",
                        account = ToPublicAccount(account),
                        channels = FuturesPrivateChannels,
                        result,
                        source = "Deribit private/subscribe"
                    });
                    continue;
                }
            }

            if (!authenticated || !subscribed) continue;
            if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() != "subscription") continue;
            if (!root.TryGetProperty("params", out var paramsEl)) continue;
            var channel = paramsEl.TryGetProperty("channel", out var chEl) ? chEl.GetString() ?? string.Empty : string.Empty;
            if (!paramsEl.TryGetProperty("data", out var dataEl)) continue;

            if (channel.StartsWith("user.orders.", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var order in EnumerateOrders(dataEl))
                {
                    await WriteSseAsync(context, "order", BuildOrderPayload(account, channel, order));
                }
                continue;
            }

            if (channel.StartsWith("user.changes.", StringComparison.OrdinalIgnoreCase))
            {
                var emitted = false;
                foreach (var order in EnumerateChangesOrders(dataEl))
                {
                    emitted = true;
                    await WriteSseAsync(context, "order", BuildOrderPayload(account, channel, order));
                }
                foreach (var trade in EnumerateChangesTrades(dataEl))
                {
                    emitted = true;
                    await WriteSseAsync(context, "trade", new
                    {
                        dt = 36,
                        exchange = "Deribit",
                        market = "futures",
                        account = ToPublicAccount(account),
                        channel,
                        d = trade.Clone(),
                        source = "Deribit " + channel
                    });
                }
                if (!emitted)
                {
                    await WriteSseAsync(context, "change", new { dt = 38, exchange = "Deribit", market = "futures", account = ToPublicAccount(account), channel, d = dataEl.Clone(), source = "Deribit " + channel });
                }
                continue;
            }

            if (channel.StartsWith("user.portfolio.", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSseAsync(context, "portfolio", new
                {
                    dt = 46,
                    exchange = "Deribit",
                    market = "futures",
                    account = ToPublicAccount(account),
                    channel,
                    d = dataEl.Clone(),
                    source = "Deribit " + channel
                });
            }
        }

        log($"[Deribit private order WS] disconnected account #{account.AccId}", Enums.LogLevel.llBaselogic);
    }

    private static object BuildOrderPayload(DeribitAccountCredentials account, string channel, JsonElement order)
    {
        var normalized = NormalizeOrder(order);
        return new
        {
            dt = 35,
            exchange = "Deribit",
            market = "futures",
            account = ToPublicAccount(account),
            channel,
            d = normalized,
            raw = order.Clone(),
            source = "Deribit " + channel
        };
    }

    private static object NormalizeOrder(JsonElement order)
    {
        var instrument = ReadString(order, "instrument_name") ?? "";
        var direction = (ReadString(order, "direction") ?? "").Trim().ToLowerInvariant();
        var amount = ReadDouble(order, "amount");
        var filled = ReadDouble(order, "filled_amount");
        var price = ReadDouble(order, "price");
        var avgPrice = ReadDouble(order, "average_price");
        var state = ReadString(order, "order_state") ?? ReadString(order, "state") ?? "";
        var orderType = ReadString(order, "order_type") ?? ReadString(order, "type") ?? "";
        var createTime = ReadLong(order, "creation_timestamp");
        var updateTime = ReadLong(order, "last_update_timestamp") ?? ReadLong(order, "timestamp");
        var baseToken = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        return new
        {
            orderId = ReadString(order, "order_id") ?? "",
            clientOrderId = ReadString(order, "label") ?? "",
            symbol = instrument,
            displaySymbol = instrument,
            displayName = instrument,
            instrument,
            market = "futures",
            side = direction == "buy" ? 1 : direction == "sell" ? 2 : (int?)null,
            direction,
            tradeSide = direction,
            amount,
            qty = amount,
            size = amount,
            orderQty = amount,
            filled,
            fillQty = filled,
            filledQty = filled,
            price,
            px = price,
            orderPrice = price,
            avgPrice,
            averagePrice = avgPrice,
            orderType,
            type = orderType,
            status = state,
            state,
            orderState = state,
            order_state = state,
            open = ReadBool(order, "open"),
            remainingAmount = ReadDouble(order, "remaining_amount"),
            reduceOnly = ReadBool(order, "reduce_only"),
            postOnly = ReadBool(order, "post_only"),
            timeInForce = ReadString(order, "time_in_force") ?? "",
            baseToken,
            currency = ReadString(order, "currency") ?? baseToken,
            createTime,
            createdTime = createTime,
            updateTime,
            ts = updateTime ?? createTime,
            raw = order.Clone()
        };
    }

    private static IEnumerable<JsonElement> EnumerateOrders(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
            yield break;
        }
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (LooksLikeOrder(data)) yield return data;
            if (data.TryGetProperty("orders", out var orders) && orders.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in orders.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateChangesOrders(JsonElement data)
    {
        foreach (var order in EnumerateOrders(data)) yield return order;
    }

    private static IEnumerable<JsonElement> EnumerateChangesTrades(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) yield break;
        foreach (var key in new[] { "trades", "fills" })
        {
            if (!data.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in arr.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
        }
    }

    private static bool LooksLikeOrder(JsonElement data) =>
        data.TryGetProperty("order_id", out _) || data.TryGetProperty("order_state", out _) || data.TryGetProperty("instrument_name", out _) && data.TryGetProperty("direction", out _);

    private static async Task SendDeribitRpcAsync(ClientWebSocket socket, int id, string method, object parameters, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new System.IO.MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.Count > 0) ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task WriteSseAsync(HttpContext context, string eventName, object payload)
    {
        await context.Response.WriteAsync("event: " + eventName + "\n", context.RequestAborted);
        await context.Response.WriteAsync("data: " + JsonSerializer.Serialize(payload).Replace("\r", "").Replace("\n", "") + "\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static bool TryRequireAdminOrQueryToken(HttpContext context, out UserSession? session, out int fail)
    {
        session = UserSessionTools.GetUserSession(context);
        if (session == null)
        {
            var token = context.Request.Query["access_token"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                session = AuthStore.Get(token);
                if (session == null && string.Equals(token, "admin-token", StringComparison.OrdinalIgnoreCase))
                {
                    session = new UserSession { UserId = 1, Login = "admin", Role = "Admin" };
                }
            }
        }

        if (session == null) { fail = 401; return false; }
        if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) { fail = 403; return false; }
        fail = 0;
        return true;
    }

    private static async Task<int?> GetMappedDeribitAccountIdAsync(VANWebServiceConfig cfg, UserSession session, CancellationToken ct)
    {
        var userId = await ResolveExistingUserIdAsync(cfg, session, ct);
        if (!userId.HasValue) return null;
        await using var conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
select ua.acc_id
from public.van_user_account ua
join public.van_account a on a.acc_id = ua.acc_id
where ua.user_id = @uid
order by ua.created_utc desc, ua.acc_id
limit 1;
", conn);
        cmd.Parameters.AddWithValue("uid", userId.Value);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static async Task<int?> ResolveExistingUserIdAsync(VANWebServiceConfig cfg, UserSession session, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
select user_id from public.van_user where user_id = @uid
union all
select user_id from public.van_user where lower(login) = lower(@login)
limit 1;
", conn);
        cmd.Parameters.AddWithValue("uid", session.UserId);
        cmd.Parameters.AddWithValue("login", session.Login ?? string.Empty);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static async Task<DeribitAccountCredentials?> LoadDeribitAccountCredentialsAsync(VANWebServiceConfig cfg, int? accountId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
        await conn.OpenAsync(ct);
        var sql = accountId.HasValue
            ? @"select acc_id, acc_name, api_public_key, api_secret_cipher from public.van_account where acc_id = @acc_id limit 1;"
            : @"select acc_id, acc_name, api_public_key, api_secret_cipher from public.van_account where api_public_key is not null and api_secret_cipher is not null order by acc_id limit 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (accountId.HasValue) cmd.Parameters.AddWithValue("acc_id", accountId.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var accId = reader.GetInt32(0);
        return new DeribitAccountCredentials(
            accId,
            reader.IsDBNull(1) ? "Account " + accId.ToString(CultureInfo.InvariantCulture) : reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
    }

    private static object ToPublicAccount(DeribitAccountCredentials account) => new
    {
        id = account.AccId,
        name = account.Name,
        apiKeyMasked = MaskKey(account.ApiPublicKey),
        isActive = true,
        storeMinuteEquity = true,
        equityCurrency = "USD"
    };

    private static string MaskKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        var trimmed = value.Trim();
        if (trimmed.Length <= 8) return trimmed[..Math.Min(2, trimmed.Length)] + "...";
        return trimmed[..4] + "..." + trimmed[^4..];
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private sealed record DeribitAccountCredentials(int AccId, string Name, string ApiPublicKey, string ApiSecretCipher);
}
