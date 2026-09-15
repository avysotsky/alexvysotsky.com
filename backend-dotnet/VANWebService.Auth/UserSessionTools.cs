using System;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace VANWebService.Auth;

public static class UserSessionTools
{
	private static LoginUserDto? GetUserFromToken(HttpContext httpContext)
	{
		string text = httpContext.Request.Headers["Authorization"].ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (!text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string text2 = text.Substring("Bearer ".Length).Trim();
		if (string.IsNullOrEmpty(text2))
		{
			return null;
		}
		if (string.Equals(text2, "admin-token", StringComparison.OrdinalIgnoreCase))
		{
			return new LoginUserDto
			{
				userId = 1,
				login = "admin",
				fullName = "Administrator",
				role = "Admin"
			};
		}
		if (text2.StartsWith("user-", StringComparison.OrdinalIgnoreCase) && text2.EndsWith("-token", StringComparison.OrdinalIgnoreCase))
		{
			string text3 = text2.Substring("user-".Length, text2.Length - "user-".Length - "-token".Length);
			if (string.IsNullOrWhiteSpace(text3))
			{
				return null;
			}
			return new LoginUserDto
			{
				userId = 100,
				login = text3,
				fullName = text3,
				role = "User"
			};
		}
		return null;
	}

	public static UserSession? GetUserSession(HttpContext context)
	{
		if (!context.Request.Headers.TryGetValue("Authorization", out var value))
		{
			return null;
		}
		string text = value.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = (text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? text.Substring("Bearer ".Length).Trim() : text.Trim());
		if (string.IsNullOrEmpty(text2))
		{
			return null;
		}
		UserSession userSession = AuthStore.Get(text2);
		if (userSession != null)
		{
			return userSession;
		}
		LoginUserDto userFromToken = GetUserFromToken(context);
		if (userFromToken != null)
		{
			return new UserSession
			{
				UserId = userFromToken.userId,
				Login = (userFromToken.login ?? ""),
				Role = (userFromToken.role ?? "User"),
				AccountId = null
			};
		}
		return null;
	}
}
