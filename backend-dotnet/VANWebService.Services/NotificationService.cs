using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace VANWebService.Services;

public sealed class NotificationService
{
	private readonly VANWebServiceConfig _cfg;

	private readonly CoreDTO.Logger.Enums.LogAction _log;

	private readonly HttpClient _http;

	public NotificationService(VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
	{
		_cfg = cfg;
		_log = log;
		_http = new HttpClient();
	}

	public async Task NotifyRegistrationAsync(PublicRegistrationRequestDTO dto)
	{
		await SendTelegramRegistrationAsync(dto);
		await SendEmailRegistrationAsync(dto);
	}

	private async Task SendTelegramRegistrationAsync(PublicRegistrationRequestDTO dto)
	{
		if (string.IsNullOrWhiteSpace(_cfg.TelegramBotToken) || string.IsNullOrWhiteSpace(_cfg.TelegramChatId))
		{
			_log("[REG][TG] Telegram config is empty, skipping telegram notification", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return;
		}
		try
		{
			string value = "New registration\nName: " + dto.fullName + "\nEmail: " + dto.email + "\nTelegram: " + (dto.telegram ?? "-") + "\nCountry: " + (dto.country ?? "-") + "\nCity: " + (dto.city ?? "-") + "\nPlanned (USD): " + (dto.plannedInvestmentUsd?.ToString("0") ?? "-") + "\nComment: " + (dto.comment ?? "-");
			string requestUri = "https://api.telegram.org/bot" + _cfg.TelegramBotToken + "/sendMessage";
			Dictionary<string, string> nameValueCollection = new Dictionary<string, string>
			{
				["chat_id"] = _cfg.TelegramChatId,
				["text"] = value
			};
			using HttpResponseMessage resp = await _http.PostAsync(requestUri, new FormUrlEncodedContent(nameValueCollection));
			if (!resp.IsSuccessStatusCode)
			{
				string value2 = await resp.Content.ReadAsStringAsync();
				_log($"[REG][TG][ERROR] status={resp.StatusCode}, body={value2}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			else
			{
				_log("[REG][TG] Telegram notification sent", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
		}
		catch (Exception value3)
		{
			_log($"[REG][TG][ERROR] Exception: {value3}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
		}
	}

	private async Task SendEmailRegistrationAsync(PublicRegistrationRequestDTO dto)
	{
		if (string.IsNullOrWhiteSpace(_cfg.NotificationEmailTo))
		{
			_log("[REG][MAIL] NotificationEmailTo is empty – skip e-mail", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return;
		}
		string to = _cfg.NotificationEmailTo.Trim();
		string subject = "New registration: " + dto.email;
		string bodyText = "New registration request\r\n\r\nFull name: " + dto.fullName + "\r\nEmail:     " + dto.email + "\r\n";
		await SendEmailAsync(to, "VAN WebService", subject, bodyText, "REG");
	}

	public async Task NotifyContactUsAsync(PublicContactUsRequestDTO dto, string? ip, string? userAgent, CancellationToken ct)
	{
		await SendTelegramContactUsAsync(dto, ip, userAgent, ct);
		bool operatorMailSent = await SendEmailContactUsAsync(dto, ip, userAgent, ct);
		bool acknowledgementSent = await SendContactAcknowledgementAsync(dto, ct);
		Console.WriteLine("[CONTACT][MAIL][RESULT] operator=" + operatorMailSent + ", ack=" + acknowledgementSent + ", sender=" + (dto?.email ?? "-"));
	}

	private async Task SendTelegramContactUsAsync(PublicContactUsRequestDTO dto, string? ip, string? userAgent, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(_cfg.TelegramBotToken) || string.IsNullOrWhiteSpace(_cfg.ContactTelegramChatId))
		{
			_log("[CONTACT][TG] Telegram config is empty, skipping telegram notification", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return;
		}
		try
		{
			string value = new StringBuilder().AppendLine("New Contact Us message").AppendLine("Name: " + (dto.name ?? "-")).AppendLine("Email: " + (dto.email ?? "-"))
				.AppendLine("IP: " + (ip ?? "-"))
				.AppendLine("User-Agent: " + (string.IsNullOrWhiteSpace(userAgent) ? "-" : userAgent))
				.AppendLine()
				.AppendLine("Message:")
				.AppendLine(dto.message ?? "")
				.ToString();
			string requestUri = "https://api.telegram.org/bot" + _cfg.TelegramBotToken + "/sendMessage";
			Dictionary<string, string> nameValueCollection = new Dictionary<string, string>
			{
				["chat_id"] = _cfg.ContactTelegramChatId,
				["text"] = value
			};
			using HttpResponseMessage resp = await _http.PostAsync(requestUri, new FormUrlEncodedContent(nameValueCollection), ct);
			if (!resp.IsSuccessStatusCode)
			{
				string value2 = await resp.Content.ReadAsStringAsync(ct);
				_log($"[CONTACT][TG][ERROR] status={resp.StatusCode}, body={value2}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			else
			{
				_log("[CONTACT][TG] Telegram notification sent", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
		}
		catch (Exception value3)
		{
			_log($"[CONTACT][TG][ERROR] Exception: {value3}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
		}
	}

	private async Task<bool> SendEmailContactUsAsync(PublicContactUsRequestDTO dto, string? ip, string? userAgent, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(_cfg.ContactEmailTo))
		{
			_log("[CONTACT][MAIL] ContactEmailTo is empty – skip e-mail", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return false;
		}
		string to = _cfg.ContactEmailTo.Trim();
		string subject = "Contact us: " + dto.email;
		string bodyText = "New Contact Us message\r\n\r\nName:       " + dto.name + "\r\nEmail:      " + dto.email + "\r\nIP:         " + (ip ?? "-") + "\r\nUser-Agent: " + (string.IsNullOrWhiteSpace(userAgent) ? "-" : userAgent) + "\r\n\r\nMessage:\r\n" + dto.message;
		return await SendEmailAsync(to, "VAN WebService", subject, bodyText, "CONTACT", ct, dto.email, dto.name);
	}

	private async Task<bool> SendContactAcknowledgementAsync(PublicContactUsRequestDTO dto, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(dto?.email))
		{
			_log("[CONTACT][ACK] Sender email is empty – skip acknowledgement", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return false;
		}
		string to = dto.email.Trim();
		string safeName = string.IsNullOrWhiteSpace(dto.name) ? "there" : dto.name.Trim();
		string subject = "We received your message";
		string bodyText = "Hello " + safeName + ",\r\n\r\nThank you for contacting VAN. This is an automatic confirmation that we received your message and will review it shortly.\r\n\r\nYour email: " + to + "\r\nYour message:\r\n" + (dto.message ?? string.Empty) + "\r\n\r\nBest regards,\r\nVAN";
		return await SendEmailAsync(to, "VAN", subject, bodyText, "CONTACT-ACK", ct);
	}

	private async Task<bool> SendEmailAsync(string to, string fromDisplayName, string subject, string bodyText, string tag, CancellationToken ct = default(CancellationToken), string? replyToEmail = null, string? replyToName = null)
	{
		if (string.IsNullOrWhiteSpace(_cfg.SmtpHost) || string.IsNullOrWhiteSpace(_cfg.SmtpUser) || string.IsNullOrWhiteSpace(_cfg.SmtpPassword))
		{
			_log("[" + tag + "][MAIL] SMTP config is incomplete – skip e-mail", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return false;
		}
		try
		{
			string host = _cfg.SmtpHost.Trim();
			int port = _cfg.SmtpPort;
			string user = _cfg.SmtpUser.Trim();
			string pass = _cfg.SmtpPassword;
			SecureSocketOptions secure = (_cfg.SmtpUseSsl ? ((port == 465) ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.None);
			MimeMessage message = new MimeMessage
			{
				From = { (InternetAddress)new MailboxAddress(fromDisplayName, user) },
				To = { (InternetAddress)MailboxAddress.Parse(to) },
				Subject = subject,
				Body = new TextPart("plain")
				{
					Text = (bodyText ?? "")
				}
			};
			if (!string.IsNullOrWhiteSpace(replyToEmail))
			{
				message.ReplyTo.Add(new MailboxAddress(replyToName ?? string.Empty, replyToEmail.Trim()));
			}
			using SmtpClient smtp = new SmtpClient
			{
				Timeout = 15000
			};
			await smtp.ConnectAsync(host, port, secure, ct);
			smtp.AuthenticationMechanisms.Remove("XOAUTH2");
			await smtp.AuthenticateAsync(user, pass, ct);
			await smtp.SendAsync(message, ct);
			await smtp.DisconnectAsync(quit: true, ct);
			Console.WriteLine($"[{tag}][MAIL] E-mail sent to {to} via {host}:{port} ({secure})");
			_log($"[{tag}][MAIL] E-mail sent to {to} via {host}:{port} ({secure})", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return true;
		}
		catch (Exception value)
		{
			Console.WriteLine($"[{tag}][MAIL][ERROR] {value}");
			_log($"[{tag}][MAIL][ERROR] {value}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return false;
		}
	}
	// ── Anomaly Guard Alerts ────────────────────────────────────────────────

	/// <summary>
	/// Alert operator when today's daily equity row is anomalous and enters retry.
	/// </summary>
	public async Task NotifyAnomalyGuardRetryAsync(int accId, DateOnly date, int attempt, int maxAttempts, string reason, CancellationToken ct = default)
	{
		string subject = $"[VAN][ANOMALY-GUARD] accId={accId} date={date:yyyy-MM-dd} deferred (attempt {attempt}/{maxAttempts})";
		string body = $"VAN daily equity anomaly guard triggered.\n\nAccount: {accId}\nDate: {date:yyyy-MM-dd}\nAttempt: {attempt}/{maxAttempts}\nReason: {reason}\n\nToday's row has NOT been written yet. Retrying in ~60s.";
		await SendTelegramOperatorAlertAsync(subject + "\n\n" + body, "[ANOMALY-GUARD]", ct);
		if (!string.IsNullOrWhiteSpace(_cfg.NotificationEmailTo))
			await SendEmailAsync(_cfg.NotificationEmailTo.Trim(), "VAN WebService", subject, body, "ANOMALY-GUARD", ct);
	}

	/// <summary>
	/// Alert operator when retries are exhausted and today's row was not written.
	/// </summary>
	public async Task NotifyAnomalyGuardExhaustedAsync(int accId, DateOnly date, int maxAttempts, string reason, CancellationToken ct = default)
	{
		string subject = $"[VAN][ANOMALY-GUARD][CRITICAL] accId={accId} date={date:yyyy-MM-dd} NOT written after {maxAttempts} attempts";
		string body = $"VAN daily equity anomaly guard EXHAUSTED.\n\nAccount: {accId}\nDate: {date:yyyy-MM-dd}\nAttempts exhausted: {maxAttempts}\nLast reason: {reason}\n\nToday's daily equity row was NOT written. It will retry on next scheduled run (tomorrow ~08:01 UTC).";
		await SendTelegramOperatorAlertAsync(subject + "\n\n" + body, "[ANOMALY-GUARD][CRITICAL]", ct);
		if (!string.IsNullOrWhiteSpace(_cfg.NotificationEmailTo))
			await SendEmailAsync(_cfg.NotificationEmailTo.Trim(), "VAN WebService", subject, body, "ANOMALY-GUARD-CRIT", ct);
	}


	public async Task NotifyCoincallSpotRestReconcileAsync(string accountName, int accountId, string symbol, string reason, string page, CancellationToken ct = default)
	{
		string safeAccount = string.IsNullOrWhiteSpace(accountName) ? "-" : accountName.Trim();
		string safeSymbol = string.IsNullOrWhiteSpace(symbol) ? "-" : symbol.Trim().ToUpperInvariant();
		string safeReason = string.IsNullOrWhiteSpace(reason) ? "spot-external-cancel-reconcile" : reason.Trim();
		string safePage = string.IsNullOrWhiteSpace(page) ? "CoinCall Spread Workstation" : page.Trim();
		string text = $"[VAN][CoinCall][REST]\nSpot external-cancel reconcile REST request\nPage: {safePage}\nFunction: spot open-orders reconcile\nSymbol: {safeSymbol}\nReason: {safeReason}\nAccount: {safeAccount} (id={accountId})\nUTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
		await SendTelegramOperatorAlertAsync(text, "[COINCALL][SPOT-REST-RECONCILE]", ct);
	}

	private async Task SendTelegramOperatorAlertAsync(string text, string tag, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(_cfg.TelegramBotToken) || string.IsNullOrWhiteSpace(_cfg.TelegramChatId))
		{
			_log($"[{tag}][TG] Telegram config is empty, skipping alert", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return;
		}
		try
		{
			string requestUri = "https://api.telegram.org/bot" + _cfg.TelegramBotToken + "/sendMessage";
			var payload = new Dictionary<string, string>
			{
				["chat_id"] = _cfg.TelegramChatId,
				["text"] = text
			};
			using HttpResponseMessage resp = await _http.PostAsync(requestUri, new FormUrlEncodedContent(payload), ct);
			if (!resp.IsSuccessStatusCode)
			{
				string body = await resp.Content.ReadAsStringAsync(ct);
				_log($"[{tag}][TG][ERROR] status={resp.StatusCode}, body={body}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			else
			{
				_log($"[{tag}][TG] Alert sent", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
		}
		catch (Exception ex)
		{
			_log($"[{tag}][TG][ERROR] Exception: {ex}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
		}
	}

}
