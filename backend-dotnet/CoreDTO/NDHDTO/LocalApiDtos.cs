using System;
using System.Collections.Generic;

namespace CoreDTO.NDHDTO
{
    public class EquityCurvePointDTO
    {
        public DateTime dt { get; set; }
        public double equityUsd { get; set; }
    }

    public class EquityCurveDTO
    {
        public List<EquityCurvePointDTO> points { get; set; } = new();
    }

    public class UserAccountDTO
    {
        public int accountId { get; set; }
        public string? name { get; set; }
        public double equityUsd { get; set; }
        public double pnlUsd { get; set; }
        public double depositsUsd { get; set; }
        public double withdrawalsUsd { get; set; }
        public double investmentUsd { get; set; }
    }

    public class UserProfileDTO
    {
        public string fullName { get; set; } = "";
        public string email { get; set; } = "";
        public string? telegram { get; set; }
        public string? country { get; set; }
        public string? city { get; set; }
    }

    public class UserDashboardResponseDTO
    {
        public LoginUserDTO? user { get; set; }
        public UserProfileDTO? profile { get; set; }
        public UserAccountDTO? account { get; set; }
        public EquityCurveDTO? equityCurve { get; set; }
    }

    public class AdminAccountDTO
    {
        public int accountId { get; set; }
        public string? name { get; set; }
        public double equityUsd { get; set; }
        public double pnlUsd { get; set; }
        public List<AdminCidDTO> cids { get; set; } = new();
    }

    public class AdminCidDTO
    {
        public long cidId { get; set; }
        public string? name { get; set; }
        public double feeRatePercents { get; set; }
    }

    public class AdminDashboardResponseDTO
    {
        public EquityCurveDTO? fundEquity { get; set; }
        public List<AdminAccountDTO> accounts { get; set; } = new();
    }

    //public class FundEquityCurveResponseDTO
    //{
    //    public List<CoreDTO.NDHDTO.FundEquityPointDTO> points { get; set; } = new();
    //}

    public class PublicRegistrationRequestDTO
    {
        public string fullName { get; set; } = "";
        public string email { get; set; } = "";
        public string? telegram { get; set; }
        public string? country { get; set; }
        public string? city { get; set; }
        public double? plannedInvestmentUsd { get; set; }
        public string? comment { get; set; }
        public string? phone { get; set; }
    }

    public class PublicRegistrationResponseDTO
    {
        public bool ok { get; set; }
    }

    public class PublicRegistrationListItemDTO
    {
        public int id { get; set; }
        public DateTime createdAtUtc { get; set; }
        public string fullName { get; set; } = "";
        public string email { get; set; } = "";
        public string? telegram { get; set; }
        public string? country { get; set; }
        public string? city { get; set; }
        public double? plannedInvestmentUsd { get; set; }
        public string? comment { get; set; }
    }

    public class PublicRegistrationListResponseDTO
    {
        public List<PublicRegistrationListItemDTO> items { get; set; } = new();
    }

    public class DeleteRegistrationRequestDTO
    {
        public int registrationId { get; set; }
    }

    public class AdminUserListItemDTO
    {
        public int userId { get; set; }
        public string login { get; set; } = "";
        public string fullName { get; set; } = "";
        public string role { get; set; } = "";
        public bool isActive { get; set; }
        public DateTime timestampUtc { get; set; }

        public double? investmentUsd { get; set; }
        public string accountType { get; set; } = "";
        public int? segregatedAccountId { get; set; }

        public long? cidId { get; set; } // ✅ NEW in list

        public int[] accIds { get; set; } = Array.Empty<int>();
    }

    public class AdminUserListResponseDTO
    {
        public List<AdminUserListItemDTO> items { get; set; } = new();
    }

    public class DeleteUserRequestDTO
    {
        public int userId { get; set; }
    }

    public class ResetUserApiKeysRequestDTO
    {
        public int userId { get; set; }
    }

    public class ResetUserApiKeysResponseDTO
    {
        public bool ok { get; set; }
        public string? publicKey { get; set; }
        public string? secretKeyLast4 { get; set; }
        public string? secretKey { get; set; }
        public string? error { get; set; }
    }
}
