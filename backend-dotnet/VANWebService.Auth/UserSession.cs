namespace VANWebService.Auth;

public sealed class UserSession
{
	public int UserId { get; set; }

	public string Login { get; set; } = "";

	public string Role { get; set; } = "User";

	public int? AccountId { get; set; }
}
