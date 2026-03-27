using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Replica.Api.Contracts;
using Replica.Api.Controllers;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class AuthControllerTests
{
    [Fact]
    public void GetCurrentUser_ReturnsResolvedActorSnapshot()
    {
        var controller = CreateController();

        ReplicaApiCurrentUserContext.Set(controller.ControllerContext.HttpContext, new ReplicaApiCurrentUser
        {
            Name = "Andrew",
            Role = ReplicaApiRoles.Admin,
            IsAuthenticated = true,
            IsValidated = true,
            AuthScheme = "Header"
        });

        var result = controller.GetCurrentUser();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AuthMeResponse>(ok.Value);
        Assert.Equal("Andrew", payload.Name);
        Assert.Equal(ReplicaApiRoles.Admin, payload.Role);
        Assert.True(payload.IsAuthenticated);
        Assert.True(payload.IsValidated);
        Assert.True(payload.CanManageUsers);
        Assert.Equal("Header", payload.AuthScheme);
    }

    private static AuthController CreateController()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var tokenService = new ReplicaApiTokenService(configuration, NullLogger<ReplicaApiTokenService>.Instance);

        return new AuthController(new InMemoryLanOrderStore(), tokenService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
