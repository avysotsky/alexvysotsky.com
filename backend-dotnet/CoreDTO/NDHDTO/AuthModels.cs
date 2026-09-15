using System.Text.Json.Serialization;

namespace CoreDTO.NDHDTO
{
    public class LoginUserDTO
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        // "Admin" / "User"
        [JsonPropertyName("role")]
        public string Role { get; set; } = "User";
    }

    public class LoginRequestDTO
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponseDTO
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("user")]
        public LoginUserDTO User { get; set; } = new();
    }
}
