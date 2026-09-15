using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VANWebService.Services;

public static class BinanceApiClient
{
    private const string BaseUrl = "https://fapi.binance.com";
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    static BinanceApiClient()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VANWebService/1.0");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public static async Task<JsonDocument> GetPublicAsync(string pathAndQuery, CancellationToken ct = default)
    {
        string path = pathAndQuery.StartsWith("/", StringComparison.Ordinal) ? pathAndQuery : "/" + pathAndQuery;
        using var response = await Http.GetAsync(BaseUrl + path, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Binance HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonDocument.Parse(body);
    }
}
