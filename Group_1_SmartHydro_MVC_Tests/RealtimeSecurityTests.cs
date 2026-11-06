using System.Security.Claims;
using Group_1_SmartHydro_MVC.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC_Tests;

public class RealtimeSecurityTests
{
    [Fact]
    public void Token_UsesOnlyTheSignedInUsersClaim()
    {
        var controller = new RealtimeController { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedResult>(controller.Token());
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("access_token", "test-token")], "Test"));
        Assert.IsType<JsonResult>(controller.Token());
        Assert.NotNull(typeof(RealtimeController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).SingleOrDefault());
    }
    [Fact]
    public void TokenResponse_IsNeverCached()
    {
        var attribute = Assert.IsType<ResponseCacheAttribute>(typeof(RealtimeController).GetMethod("Token")!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), true).Single());
        Assert.True(attribute.NoStore); Assert.Equal(ResponseCacheLocation.None, attribute.Location);
    }
}
