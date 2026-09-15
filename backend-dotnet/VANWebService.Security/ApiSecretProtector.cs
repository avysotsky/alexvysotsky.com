using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VANWebService.Security;

internal static class ApiSecretProtector
{
	public static string Encrypt(string plainText, string keyMaterial)
	{
		if (string.IsNullOrEmpty(plainText))
		{
			throw new ArgumentException("plainText is empty");
		}
		using Aes aes = Aes.Create();
		aes.Key = DeriveKey(keyMaterial);
		aes.GenerateIV();
		using MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(aes.IV, 0, aes.IV.Length);
		using (CryptoStream stream = new CryptoStream(memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
		{
			using StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8);
			streamWriter.Write(plainText);
		}
		return Convert.ToBase64String(memoryStream.ToArray());
	}

	public static string Decrypt(string cipherTextBase64, string keyMaterial)
	{
		if (string.IsNullOrEmpty(cipherTextBase64))
		{
			throw new ArgumentException("cipherTextBase64 is empty");
		}
		byte[] array = Convert.FromBase64String(cipherTextBase64);
		using Aes aes = Aes.Create();
		aes.Key = DeriveKey(keyMaterial);
		byte[] array2 = new byte[aes.BlockSize / 8];
		Buffer.BlockCopy(array, 0, array2, 0, array2.Length);
		aes.IV = array2;
		byte[] array3 = new byte[array.Length - array2.Length];
		Buffer.BlockCopy(array, array2.Length, array3, 0, array3.Length);
		using MemoryStream stream = new MemoryStream(array3);
		using CryptoStream stream2 = new CryptoStream(stream, aes.CreateDecryptor(), CryptoStreamMode.Read);
		using StreamReader streamReader = new StreamReader(stream2, Encoding.UTF8);
		return streamReader.ReadToEnd();
	}

	private static byte[] DeriveKey(string keyMaterial)
	{
		using SHA256 sHA = SHA256.Create();
		return sHA.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
	}
}
