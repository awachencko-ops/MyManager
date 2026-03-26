namespace Replica.Api.Contracts;

public sealed class AuthMeResponse
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool IsValidated { get; set; }
    public bool CanManageUsers { get; set; }
}
