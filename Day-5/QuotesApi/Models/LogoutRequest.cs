namespace QuotesApi.Models;

public class LogoutRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
