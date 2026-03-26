namespace Replica.Api.Contracts;

public sealed class UpsertUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}
