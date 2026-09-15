namespace VANWebService.Auth;

public sealed class LoginUserDto
{
	public int userId { get; set; }

	public string? login { get; set; }

	public string? fullName { get; set; }

	public string? role { get; set; }
}
