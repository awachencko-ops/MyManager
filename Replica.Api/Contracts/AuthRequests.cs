namespace Replica.Api.Contracts;

public sealed class AuthLoginRequest
{
    public string UserName { get; set; } = string.Empty;
}

public sealed class AuthRefreshRequest
{
    public string SessionId { get; set; } = string.Empty;
}

public sealed class AuthRevokeRequest
{
    public string SessionId { get; set; } = string.Empty;
}
