namespace CoreDTO.NDHDTO
{
    public class CreateUserRequestDTO
    {
        public string login { get; set; } = "";
        public string password { get; set; } = "";

        public string? fullName { get; set; }
        public string? role { get; set; }              // "User" / "Investor" / "Admin"
        public bool isActive { get; set; } = true;

        public double? investmentUsd { get; set; }

        public string? accountType { get; set; }       // "Individual" / "Segregated"
        public int? segregatedAccountId { get; set; }

        public long? cidId { get; set; }               // ✅ NEW (int8)

        public int[]? accIds { get; set; }             // optional bind accounts сразу
    }

    public class CreateUserResponseDTO
    {
        public bool ok { get; set; }
        public int? userId { get; set; }
        public string? error { get; set; }
    }
}
