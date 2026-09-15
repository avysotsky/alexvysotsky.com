using System.Collections.Generic;

namespace CoreDTO.NDHDTO
{

    // === Login ===

    public sealed class LoginRequestDto
    {
        public string login { get; set; } = "";
        public string password { get; set; } = "";
    }

    public sealed class LoginUserDto
    {
        public int userId { get; set; }
        public string login { get; set; } = "";
        public string? fullName { get; set; }
        public string role { get; set; } = "";
    }

    // === User dashboard ===

    public sealed class UserProfileDto
    {
        public string fullName { get; set; } = "";
        public string email { get; set; } = "";
        public string? telegram { get; set; }
        public string? country { get; set; }
        public string? city { get; set; }
    }

    public sealed class UserAccountDto
    {
        public int accountId { get; set; }
        public string name { get; set; } = "";
        public decimal equityUsd { get; set; }
        public decimal pnlUsd { get; set; }
        public decimal depositsUsd { get; set; }
        public decimal withdrawalsUsd { get; set; }
        public decimal investmentUsd { get; set; }
    }

    public sealed class EquityCurveDto
    {
        public List<FundEquityPointDTO> points { get; set; } = new();
    }

    public sealed class UserDashboardResponseDto
    {
        public LoginUserDto user { get; set; } = default!;
        public UserProfileDto profile { get; set; } = default!;
        public UserAccountDto account { get; set; } = default!;
        public EquityCurveDto equityCurve { get; set; } = default!;
    }

    // === Admin dashboard ===

    public sealed class AdminAccountDto
    {
        public int accountId { get; set; }
        public string name { get; set; } = "";
        public decimal equityUsd { get; set; }
        public decimal pnlUsd { get; set; }
    }

    public sealed class AdminDashboardResponseDto
    {
        public EquityCurveDto fundEquity { get; set; } = default!;
        public List<AdminAccountDto> accounts { get; set; } = new();
    }

    // === Registration ===

    public sealed class RegistrationRequestDto
    {
        public string fullName { get; set; } = "";
        public string email { get; set; } = "";
        public string? telegram { get; set; }
        public string? country { get; set; }
        public string? city { get; set; }
        public decimal? plannedInvestmentUsd { get; set; }
        public string? comment { get; set; }
    }

    public sealed class RegistrationResponseDto
    {
        public bool ok { get; set; }
    }
}
