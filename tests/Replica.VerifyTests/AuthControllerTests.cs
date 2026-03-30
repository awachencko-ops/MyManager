using Microsoft.AspNetCore.Mvc;
using Replica.Api.Application.Abstractions;
using Replica.Api.Contracts;
using Replica.Api.Controllers;
using Xunit;

namespace Replica.VerifyTests;

public sealed class AuthControllerTests
{
    [Fact]
    public void GetCurrentUser_ReturnsResolvedActorSnapshot()
    {
        var currentUserAccessor = new FakeCurrentUserAccessor
        {
            Snapshot = new ReplicaApiCurrentUserSnapshot
            {
                Name = "Andrew",
                Role = Replica.Api.Infrastructure.ReplicaApiRoles.Admin,
                IsAuthenticated = true,
                IsValidated = true,
                CanManageUsers = true,
                AuthScheme = "Header"
            }
        };
        var controller = new AuthController(new FakeAuthService(), currentUserAccessor);

        var result = controller.GetCurrentUser();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AuthMeResponse>(ok.Value);
        Assert.Equal("Andrew", payload.Name);
        Assert.Equal(Replica.Api.Infrastructure.ReplicaApiRoles.Admin, payload.Role);
        Assert.True(payload.IsAuthenticated);
        Assert.True(payload.IsValidated);
        Assert.True(payload.CanManageUsers);
        Assert.Equal("Header", payload.AuthScheme);
    }

    private sealed class FakeCurrentUserAccessor : IReplicaApiCurrentUserAccessor
    {
        public ReplicaApiCurrentUserSnapshot Snapshot { get; set; } = new();

        public ReplicaApiCurrentUserSnapshot GetCurrentUser()
        {
            return Snapshot;
        }
    }

    private sealed class FakeAuthService : IReplicaApiAuthService
    {
        public IReadOnlyList<Replica.Shared.Models.SharedUser> GetUsers(bool includeInactive)
        {
            return Array.Empty<Replica.Shared.Models.SharedUser>();
        }

        public ReplicaApiAuthTokenIssueResult IssueToken(string userName, string role, string issuedBy, string ipAddress, string userAgent)
        {
            return ReplicaApiAuthTokenIssueResult.Failed("not implemented for this test");
        }

        public ReplicaApiAuthTokenIssueResult RefreshToken(string sessionId, string requestedBy, string ipAddress, string userAgent)
        {
            return ReplicaApiAuthTokenIssueResult.Failed("not implemented for this test");
        }

        public ReplicaApiAuthTokenRevokeResult RevokeToken(string sessionId, string requestedBy, string ipAddress, string userAgent)
        {
            return ReplicaApiAuthTokenRevokeResult.Failed("not implemented for this test");
        }
    }
}
