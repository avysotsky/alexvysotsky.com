namespace CoreDTO.NDHDTO
{
    public sealed class CreateUserFromRegistrationRequestDTO
    {
        public int registrationId { get; set; }
        public string login { get; set; } = "";
        public string password { get; set; } = "";
        public string fullName { get; set; } = "";
        public string role { get; set; } = "User"; // "User" | "Investor" | "Admin"
        public decimal? investmentUsd { get; set; } = null;

        public string accountType { get; set; } = "Individual"; // "Individual" | "Segregated"
        public int? segregatedAccountId { get; set; } = null;
    }

    public sealed class CreateUserFromRegistrationResponseDTO
    {
        public bool ok { get; set; }
        public int? userId { get; set; }
        public string? error { get; set; }
    }
}
