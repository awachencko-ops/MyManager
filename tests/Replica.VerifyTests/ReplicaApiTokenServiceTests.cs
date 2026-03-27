using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Replica.Api.Infrastructure;

namespace Replica.VerifyTests;

public sealed class ReplicaApiTokenServiceTests
{
    [Fact]
    public void IssueToken_ThenValidateToken_ReturnsUserAndRole()
    {
        var service = CreateService();

        var issueResult = service.IssueToken(
            userName: "Andrew",
            role: ReplicaApiRoles.Admin,
            issuedBy: "Andrew",
            ipAddress: "127.0.0.1",
            userAgent: "xunit");

        Assert.True(issueResult.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(issueResult.AccessToken));

        var validationResult = service.ValidateToken(issueResult.AccessToken);

        Assert.True(validationResult.IsSuccess);
        Assert.Equal("Andrew", validationResult.UserName);
        Assert.Equal(ReplicaApiRoles.Admin, validationResult.Role);
        Assert.Equal(issueResult.SessionId, validationResult.SessionId);
    }

    [Fact]
    public void RefreshToken_RotatesTokenAndInvalidatesPrevious()
    {
        var service = CreateService();
        var issued = service.IssueToken("operator-1", ReplicaApiRoles.Operator, "operator-1", "127.0.0.1", "xunit");
        Assert.True(issued.IsSuccess);

        var refreshed = service.RefreshToken(issued.SessionId, "operator-1", "127.0.0.1", "xunit");

        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(issued.AccessToken, refreshed.AccessToken);
        Assert.False(service.ValidateToken(issued.AccessToken).IsSuccess);
        Assert.True(service.ValidateToken(refreshed.AccessToken).IsSuccess);
    }

    [Fact]
    public void RevokeToken_MakesTokenInvalid()
    {
        var service = CreateService();
        var issued = service.IssueToken("operator-2", ReplicaApiRoles.Operator, "operator-2", "127.0.0.1", "xunit");
        Assert.True(issued.IsSuccess);

        var revokeResult = service.RevokeToken(issued.SessionId, "operator-2", "127.0.0.1", "xunit");

        Assert.True(revokeResult.IsSuccess);
        Assert.False(service.ValidateToken(issued.AccessToken).IsSuccess);
    }

    [Fact]
    public void ValidateToken_InvalidFormat_ReturnsFailure()
    {
        var service = CreateService();

        var validationResult = service.ValidateToken("invalid-token");

        Assert.False(validationResult.IsSuccess);
    }

    private static ReplicaApiTokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:Auth:AccessTokenLifetimeMinutes"] = "60"
            })
            .Build();

        return new ReplicaApiTokenService(configuration, NullLogger<ReplicaApiTokenService>.Instance);
    }
}
