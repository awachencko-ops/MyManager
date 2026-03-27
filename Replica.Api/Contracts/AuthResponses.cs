namespace Replica.Api.Contracts;

public sealed class AuthMeResponse
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool IsValidated { get; set; }
    public bool CanManageUsers { get; set; }
    public string AuthScheme { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}

public sealed class AuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAtUtc { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}
