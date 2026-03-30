namespace Replica.Api.Application.Abstractions;

public interface IReplicaApiCurrentUserAccessor
{
    ReplicaApiCurrentUserSnapshot GetCurrentUser();
}

public sealed class ReplicaApiCurrentUserSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool IsValidated { get; set; }
    public bool CanManageUsers { get; set; }
    public string AuthScheme { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}
