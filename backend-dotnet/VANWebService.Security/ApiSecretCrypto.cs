using System;

namespace VANWebService.Security;

public static class ApiSecretCrypto
{
	private const string EnvVarName = "VAN_API_SECRET_ENCRYPTION_KEY";

	private static string GetKeyMaterial()
	{
		string text = Environment.GetEnvironmentVariable("VAN_API_SECRET_ENCRYPTION_KEY");
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "CHANGE_ME_TO_LONG_RANDOM_SECRET_KEY_32+CHARS";
		}
		return text;
	}

	public static string Encrypt(string plainText)
	{
		return ApiSecretProtector.Encrypt(plainText, GetKeyMaterial());
	}

	public static string Decrypt(string cipherText)
	{
		return ApiSecretProtector.Decrypt(cipherText, GetKeyMaterial());
	}
}
