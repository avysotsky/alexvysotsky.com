using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VANWebService.Auth;

public static class AuthStore
{
	private static readonly ConcurrentDictionary<string, UserSession> _tokens = new ConcurrentDictionary<string, UserSession>();
	private static readonly object _fileLock = new object();
	private static readonly string _path = Environment.GetEnvironmentVariable("VAN_AUTH_STORE_PATH") ?? Path.Combine(AppContext.BaseDirectory, "auth-tokens.json");

	static AuthStore()
	{
		LoadFromDisk();
	}

	public static string CreateToken(UserSession session)
	{
		string text = Guid.NewGuid().ToString("N");
		_tokens[text] = session;
		SaveToDisk();
		return text;
	}

	public static UserSession? Get(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}
		if (!_tokens.TryGetValue(token, out UserSession value))
		{
			LoadFromDisk();
			_tokens.TryGetValue(token, out value);
		}
		return value;
	}

	private static void LoadFromDisk()
	{
		try
		{
			lock (_fileLock)
			{
				if (!File.Exists(_path)) return;
				var rows = JsonSerializer.Deserialize<Dictionary<string, UserSession>>(File.ReadAllText(_path));
				if (rows == null) return;
				foreach (var row in rows)
				{
					if (!string.IsNullOrWhiteSpace(row.Key) && row.Value != null) _tokens[row.Key] = row.Value;
				}
			}
		}
		catch
		{
			// Auth fallback remains in-memory if the persistence file is unavailable.
		}
	}

	private static void SaveToDisk()
	{
		try
		{
			lock (_fileLock)
			{
				string? dir = Path.GetDirectoryName(_path);
				if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
				string tmp = _path + ".tmp";
				File.WriteAllText(tmp, JsonSerializer.Serialize(_tokens));
				if (File.Exists(_path)) File.Delete(_path);
				File.Move(tmp, _path);
			}
		}
		catch
		{
			// Login should not fail just because the optional persistence file failed.
		}
	}
}
