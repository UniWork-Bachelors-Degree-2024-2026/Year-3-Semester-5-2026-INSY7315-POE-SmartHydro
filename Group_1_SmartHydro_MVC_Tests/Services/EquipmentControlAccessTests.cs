using System.Security.Claims;
using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC_Tests.Services;

public class EquipmentControlAccessTests
{
    private static ClaimsPrincipal UserIn(string role)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test"));
    }

    // Every signed-in role may switch relays and add doses
    [Theory]
    [InlineData("Admin")]
    [InlineData("Operator")]
    [InlineData("Worker")]
    public void CanControlEquipment_AllRoles_AreAllowed(string role)
    {
        Assert.True(UserIn(role).CanControlEquipment());
    }

    // A person with no recognised role may not
    [Fact]
    public void CanControlEquipment_NoRole_IsRefused()
    {
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).CanControlEquipment());
        Assert.False(UserIn("Visitor").CanControlEquipment());
    }

    // Creating sites and changing how a controller connects stays with admins and operators
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Operator", true)]
    [InlineData("Worker", false)]
    public void CanManageSites_WorkerStaysRefused(string role, bool expected)
    {
        Assert.Equal(expected, UserIn(role).CanManageSites());
    }
}
