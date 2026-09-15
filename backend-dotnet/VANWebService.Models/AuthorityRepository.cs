using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Npgsql;

namespace VANWebService.Models;

public class AuthorityRepository
{
	public sealed class DeribitApiKeyRecord
	{
		public string AccountName { get; set; } = "";

		public string ClientId { get; set; } = "";

		public string ClientSecret { get; set; } = "";

		public string BaseUrl { get; set; } = "https://www.deribit.com";

		public int? SubaccountId { get; set; }
	}

	private readonly string _connectionString;

	private readonly CoreDTO.Logger.Enums.LogAction _log;

	private const int PasswordHashIterations = 120000;

	private const int PasswordSaltSize = 16;

	private const int PasswordKeySize = 32;

	private const char PasswordSeparator = '.';

	public static string HashPassword(string password)
	{
		if (string.IsNullOrEmpty(password))
		{
			throw new ArgumentException("Password is empty", "password");
		}
		byte[] array = new byte[16];
		RandomNumberGenerator.Fill(array);
		using Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(password, array, 120000, HashAlgorithmName.SHA256);
		byte[] bytes = rfc2898DeriveBytes.GetBytes(32);
		return string.Join('.', "PBKDF2", 120000.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(array), Convert.ToBase64String(bytes));
	}

	public static bool VerifyPassword(string password, string stored)
	{
		if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
		{
			return false;
		}
		string[] array = stored.Split('.');
		if (array.Length != 4 || array[0] != "PBKDF2")
		{
			return false;
		}
		if (!int.TryParse(array[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return false;
		}
		byte[] salt = Convert.FromBase64String(array[2]);
		byte[] array2 = Convert.FromBase64String(array[3]);
		using Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(password, salt, result, HashAlgorithmName.SHA256);
		byte[] bytes = rfc2898DeriveBytes.GetBytes(array2.Length);
		return CryptographicOperations.FixedTimeEquals(array2, bytes);
	}

	public AuthorityRepository(string connectionString, CoreDTO.Logger.Enums.LogAction log)
	{
		_connectionString = connectionString;
		_log = log;
	}

	public async Task<LoginUserDTO?> ValidateUserAsync(string login, string password)
	{
		LoginUserDTO result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connectionString))
		{
			await conn.OpenAsync();
			LoginUserDTO loginUserDTO3;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nSELECT user_id,\r\n       login,\r\n       password,\r\n       full_name,\r\n       role\r\nFROM van_user\r\nWHERE login = @login\r\n  AND is_active = TRUE\r\nLIMIT 1;\r\n", conn))
			{
				cmd.Parameters.AddWithValue("login", login);
				LoginUserDTO loginUserDTO;
				await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
				{
					if (!(await reader.ReadAsync()))
					{
						_log("[AUTH] user '" + login + "' not found or inactive", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
						loginUserDTO = null;
					}
					else
					{
						string stored = reader.GetString(reader.GetOrdinal("password"));
						if (!VerifyPassword(password, stored))
						{
							_log("[AUTH] invalid password for '" + login + "'", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
							loginUserDTO = null;
						}
						else
						{
							LoginUserDTO loginUserDTO2 = new LoginUserDTO
							{
								UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
								Login = reader.GetString(reader.GetOrdinal("login")),
								FullName = (reader.IsDBNull(reader.GetOrdinal("full_name")) ? null : reader.GetString(reader.GetOrdinal("full_name"))),
								Role = reader.GetString(reader.GetOrdinal("role"))
							};
							_log($"[AUTH] login success for '{loginUserDTO2.Login}' (role={loginUserDTO2.Role})", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
							loginUserDTO = loginUserDTO2;
						}
					}
				}
				loginUserDTO3 = loginUserDTO;
			}
			result = loginUserDTO3;
		}
		return result;
	}

	public async Task<DeribitApiKeyRecord?> GetReferenceSelfCustodyKeysAsync()
	{
		if (string.IsNullOrWhiteSpace(_connectionString))
		{
			return null;
		}
		DeribitApiKeyRecord result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connectionString))
		{
			await conn.OpenAsync();
			DeribitApiKeyRecord deribitApiKeyRecord2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect account_name, client_id, client_secret, base_url, subaccount_id\r\nfrom fund_settings\r\nwhere account_name = @name\r\nlimit 1;\r\n", conn))
			{
				cmd.Parameters.AddWithValue("name", "Self_Custody");
				DeribitApiKeyRecord deribitApiKeyRecord;
				await using (NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync())
				{
					deribitApiKeyRecord = ((await rdr.ReadAsync()) ? new DeribitApiKeyRecord
					{
						AccountName = rdr.GetString(0),
						ClientId = rdr.GetString(1),
						ClientSecret = rdr.GetString(2),
						BaseUrl = rdr.GetString(3),
						SubaccountId = (rdr.IsDBNull(4) ? ((int?)null) : new int?(rdr.GetInt32(4)))
					} : null);
				}
				deribitApiKeyRecord2 = deribitApiKeyRecord;
			}
			result = deribitApiKeyRecord2;
		}
		return result;
	}

	public async Task SavePublicRegistrationAsync(PublicRegistrationRequestDTO dto)
	{
		await using NpgsqlConnection conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();
		await using NpgsqlCommand cmd = new NpgsqlCommand("\r\nINSERT INTO van_public_registration\r\n(full_name, email, telegram, country, city, planned_investment_usd, comment, phone, timestamp_utc)\r\nVALUES\r\n(@full_name, @email, @telegram, @country, @city, @planned, @comment, @phone, (now() at time zone 'utc'));\r\n", conn);
		cmd.Parameters.AddWithValue("full_name", dto.fullName ?? string.Empty);
		cmd.Parameters.AddWithValue("email", dto.email ?? string.Empty);
		cmd.Parameters.AddWithValue("telegram", ((object)dto.telegram) ?? ((object)DBNull.Value));
		cmd.Parameters.AddWithValue("country", ((object)dto.country) ?? ((object)DBNull.Value));
		cmd.Parameters.AddWithValue("city", ((object)dto.city) ?? ((object)DBNull.Value));
		cmd.Parameters.AddWithValue("planned_investment_usd", ((object)dto.plannedInvestmentUsd) ?? DBNull.Value);
		cmd.Parameters.AddWithValue("comment", ((object)dto.comment) ?? ((object)DBNull.Value));
		cmd.Parameters.AddWithValue("phone", ((object)dto.phone) ?? ((object)DBNull.Value));
		await cmd.ExecuteNonQueryAsync();
	}

	public Task SavePublicContactUsAsync(PublicContactUsRequestDTO dto, string? ip, string? userAgent, CancellationToken ct = default(CancellationToken))
	{
		return SavePublicContactMessageAsync(dto, ip, userAgent, ct);
	}

	public async Task SavePublicContactMessageAsync(PublicContactUsRequestDTO dto, string? ip, string? userAgent, CancellationToken ct = default(CancellationToken))
	{
		await using NpgsqlConnection conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into public.van_public_contact_message\r\n(name, email, message, ip, user_agent, timestamp_utc)\r\nvalues\r\n(@name, @email, @message, @ip, @user_agent, (now() at time zone 'utc'));\r\n", conn);
		cmd.Parameters.AddWithValue("name", (dto.name ?? string.Empty).Trim());
		cmd.Parameters.AddWithValue("email", (dto.email ?? string.Empty).Trim());
		cmd.Parameters.AddWithValue("message", (dto.message ?? string.Empty).Trim());
		cmd.Parameters.AddWithValue("ip", ((object)ip) ?? ((object)DBNull.Value));
		cmd.Parameters.AddWithValue("user_agent", ((object)userAgent) ?? ((object)DBNull.Value));
		await cmd.ExecuteNonQueryAsync(ct);
	}
}
