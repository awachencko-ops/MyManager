using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Replica.Api.Contracts;
using Replica.Api.Controllers;
using Replica.Api.Infrastructure;
using Xunit;

namespace Replica.VerifyTests;

public sealed class AuthControllerTests
{
    [Fact]
    public void GetCurrentUser_ReturnsResolvedActorSnapshot()
    {
        var controller = new AuthController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        ReplicaApiCurrentUserContext.Set(controller.ControllerContext.HttpContext, new ReplicaApiCurrentUser
        {
            Name = "Andrew",
            Role = ReplicaApiRoles.Admin,
            IsAuthenticated = true,
            IsValidated = true
        });

        var result = controller.GetCurrentUser();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AuthMeResponse>(ok.Value);
        Assert.Equal("Andrew", payload.Name);
        Assert.Equal(ReplicaApiRoles.Admin, payload.Role);
        Assert.True(payload.IsAuthenticated);
        Assert.True(payload.IsValidated);
        Assert.True(payload.CanManageUsers);
    }
}
