using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace VANWebService.Security;

public static class ApiKeysTools
{
	public static (string publicKey, string secretKey) GenerateApiKeys()
	{
		string item = "pub_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		string item2 = "sec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		return (publicKey: item, secretKey: item2);
	}

	public static string HashSecretKey(string secret)
	{
		return ApiSecretCrypto.Encrypt(secret);
	}

	public static string DecryptSecretHash(string secretHash)
	{
		return ApiSecretCrypto.Decrypt(secretHash);
	}

	public static async Task<UserApiKeysDTO?> GetUserApiKeysAsync(long userId, string connectionString, CancellationToken ct = default(CancellationToken))
	{
		UserApiKeysDTO result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(connectionString))
		{
			await conn.OpenAsync(ct);
			UserApiKeysDTO userApiKeysDTO2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect api_public_key,\r\n       api_secret_hash\r\nfrom van_user\r\nwhere user_id = @id\r\n  and is_active = true\r\nlimit 1;\r\n", conn))
			{
				cmd.Parameters.AddWithValue("id", userId);
				UserApiKeysDTO userApiKeysDTO;
				await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct))
				{
					if (!(await reader.ReadAsync(ct)))
					{
						userApiKeysDTO = null;
					}
					else
					{
						string text = (reader.IsDBNull(0) ? null : reader.GetString(0));
						string text2 = (reader.IsDBNull(1) ? null : reader.GetString(1));
						if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
						{
							userApiKeysDTO = null;
						}
						else
						{
							string apiSecretPlain = DecryptSecretHash(text2);
							userApiKeysDTO = new UserApiKeysDTO(text, apiSecretPlain);
						}
					}
				}
				userApiKeysDTO2 = userApiKeysDTO;
			}
			result = userApiKeysDTO2;
		}
		return result;
	}
}
