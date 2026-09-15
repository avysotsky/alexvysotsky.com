using System;
using System.Globalization;

namespace VANWebService.Models;

public static class ClearingScheduleTools
{
	public static (bool Ok, string Error, DateTime? NearestUtc, TimeSpan? ParsedTimeUtc) TryComputeNearestClearingTimestampUtc(bool clearingEnabled, string? clearingPeriod, string? clearingTimeUtc, int? clearingDayOfWeek, int? clearingDayOfMonth)
	{
		string text = (clearingPeriod ?? "None").Trim();
		if (!TryParseTimeUtc(clearingTimeUtc, out TimeSpan timeUtc, out string error))
		{
			return (Ok: false, Error: error, NearestUtc: null, ParsedTimeUtc: null);
		}
		if (!clearingEnabled || text.Equals("None", StringComparison.OrdinalIgnoreCase))
		{
			return (Ok: true, Error: "", NearestUtc: null, ParsedTimeUtc: timeUtc);
		}
		DateTime utcNow = DateTime.UtcNow;
		DateTime value;
		if (text.Equals("Daily", StringComparison.OrdinalIgnoreCase))
		{
			DateTime dateTime = utcNow.Date.Add(timeUtc);
			if (dateTime <= utcNow)
			{
				dateTime = dateTime.AddDays(1.0);
			}
			value = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
		}
		else if (text.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
		{
			bool flag;
			switch (clearingDayOfWeek)
			{
			default:
				flag = true;
				break;
			case 1:
			case 2:
			case 3:
			case 4:
			case 5:
			case 6:
			case 7:
				flag = false;
				break;
			}
			if (flag)
			{
				return (Ok: false, Error: "Weekly requires clearingDayOfWeek in range 1..7 (Mon..Sun).", NearestUtc: null, ParsedTimeUtc: null);
			}
			DayOfWeek dayOfWeek = ToDotNetDayOfWeek(clearingDayOfWeek.Value);
			DateTime date = utcNow.Date;
			int num = (dayOfWeek - date.DayOfWeek + 7) % 7;
			DateTime dateTime2 = date.AddDays(num).Add(timeUtc);
			if (num == 0 && dateTime2 <= utcNow)
			{
				dateTime2 = dateTime2.AddDays(7.0);
			}
			value = DateTime.SpecifyKind(dateTime2, DateTimeKind.Utc);
		}
		else
		{
			if (!text.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
			{
				return (Ok: false, Error: "Unknown clearingPeriod='" + clearingPeriod + "'. Expected: None|Daily|Weekly|Monthly.", NearestUtc: null, ParsedTimeUtc: null);
			}
			bool flag;
			switch (clearingDayOfMonth)
			{
			default:
				flag = true;
				break;
			case 1:
			case 2:
			case 3:
			case 4:
			case 5:
			case 6:
			case 7:
			case 8:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 14:
			case 15:
			case 16:
			case 17:
			case 18:
			case 19:
			case 20:
			case 21:
			case 22:
			case 23:
			case 24:
			case 25:
			case 26:
			case 27:
			case 28:
			case 29:
			case 30:
			case 31:
				flag = false;
				break;
			}
			if (flag)
			{
				return (Ok: false, Error: "Monthly requires clearingDayOfMonth in range 1..31.", NearestUtc: null, ParsedTimeUtc: null);
			}
			value = ComputeMonthly(utcNow, timeUtc, clearingDayOfMonth.Value);
		}
		return (Ok: true, Error: "", NearestUtc: value, ParsedTimeUtc: timeUtc);
	}

	private static bool TryParseTimeUtc(string? s, out TimeSpan timeUtc, out string error)
	{
		error = "";
		timeUtc = default(TimeSpan);
		string text = (s ?? "").Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return Fail("clearingTimeUtc is empty. Expected 'HH:mm' or 'HH:mm:ss'.", out timeUtc, out error);
		}
		if (TimeSpan.TryParseExact(text, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out timeUtc))
		{
			return true;
		}
		if (TimeSpan.TryParseExact(text, "hh\\:mm", CultureInfo.InvariantCulture, out timeUtc))
		{
			return true;
		}
		return Fail("Invalid clearingTimeUtc='" + s + "'. Expected 'HH:mm' or 'HH:mm:ss'.", out timeUtc, out error);
		static bool Fail(string msg, out TimeSpan t, out string err)
		{
			t = default(TimeSpan);
			err = msg;
			return false;
		}
	}

	private static DayOfWeek ToDotNetDayOfWeek(int d)
	{
		if (d != 7)
		{
			return (DayOfWeek)d;
		}
		return DayOfWeek.Sunday;
	}

	private static DateTime ComputeMonthly(DateTime nowUtc, TimeSpan timeUtc, int day)
	{
		int year = nowUtc.Year;
		int month = nowUtc.Month;
		DateTime dateTime = BuildMonthlyCandidateUtc(year, month, day, timeUtc);
		if (dateTime <= nowUtc)
		{
			DateTime dateTime2 = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
			dateTime = BuildMonthlyCandidateUtc(dateTime2.Year, dateTime2.Month, day, timeUtc);
		}
		return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
	}

	private static DateTime BuildMonthlyCandidateUtc(int year, int month, int day, TimeSpan timeUtc)
	{
		int val = DateTime.DaysInMonth(year, month);
		int day2 = Math.Min(Math.Max(day, 1), val);
		return new DateTime(year, month, day2, 0, 0, 0, DateTimeKind.Utc).Add(timeUtc);
	}
}
