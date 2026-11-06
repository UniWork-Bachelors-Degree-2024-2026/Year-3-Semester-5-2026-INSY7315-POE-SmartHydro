using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class DeviceKeySecurityTests
{
    const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    static async Task<(DefaultHttpContext Context, bool Passed)> Request(string path, string method, string? supplied, string? configured = Key)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        context.Response.Body = new MemoryStream(); context.Request.Path = path; context.Request.Method = method;
        if (supplied != null) context.Request.Headers[DeviceKeySecurity.Header] = supplied;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:DeviceKey"] = configured }).Build();
        bool passed = false;
        await DeviceKeySecurity.InvokeAsync(context, config, _ => { passed = true; return Task.CompletedTask; });
        return (context, passed);
    }
    [Theory]
    [InlineData("/api/telemetry", "POST")]
    [InlineData("/api/telemetry/", "POST")]
    [InlineData("/API/TELEMETRY/LORA", "POST")]
    [InlineData("/api/telemetry/lora/poll", "GET")]
    [InlineData("/api/dosing/callback", "POST")]
    public async Task DeviceRoutes_FailClosedAndAllowOnlyMatchingKey(string path, string method)
    {
        Assert.False((await Request(path, method, null)).Passed);
        var wrong = await Request(path, method, "wrong"); Assert.Equal(401, wrong.Context.Response.StatusCode); Assert.False(wrong.Passed);
        Assert.True((await Request(path, method, Key)).Passed);
        var missing = await Request(path, method, Key, null); Assert.Equal(503, missing.Context.Response.StatusCode); Assert.False(missing.Passed);
    }
    [Theory]
    [InlineData("/hubs/actuator")]
    [InlineData("/hubs/actuators/negotiate")]
    public async Task ValidDeviceKey_CreatesHubIdentityOnly(string path)
    {
        var result = await Request(path, "GET", Key);
        Assert.True(result.Passed); Assert.True(result.Context.User.Identity!.IsAuthenticated);
        Assert.True(result.Context.User.HasClaim("device", "true"));
        var wrong = await Request(path, "GET", "wrong"); Assert.False(wrong.Context.User.Identity?.IsAuthenticated ?? false);
    }
    [Fact]
    public async Task UserReadRoutes_KeepTheirExistingAuthentication()
    {
        var result = await Request("/api/telemetry/activity/AA:BB:CC:DD:EE:FF", "GET", null);
        Assert.True(result.Passed); Assert.False(result.Context.User.HasClaim("device", "true"));
    }
    [Fact]
    public void EmptyOrShortConfiguration_CannotAuthenticate()
    {
        Assert.False(DeviceKeySecurity.Matches("", "")); Assert.False(DeviceKeySecurity.Matches("short", "short"));
        Assert.False(DeviceKeySecurity.Matches(Key, Key + "x")); Assert.True(DeviceKeySecurity.Matches(Key, Key));
    }

    [Fact]
    public async Task BrowserIdentity_CannotReportPhysicalStateOrAcknowledgeCommands()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        var caller = new Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>();
        caller.SetupGet(x => x.User).Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test")));
        var hub = new SmartHydroAPI.Hubs.ActuatorHub(
            Moq.Mock.Of<SmartHydroAPI.Patterns.Decorator.IActuatorService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartHydroAPI.Hubs.ActuatorHub>.Instance,
            db, Moq.Mock.Of<Microsoft.AspNetCore.SignalR.IHubContext<SmartHydroAPI.Hubs.ActuatorHub>>()) { Context = caller.Object };
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => hub.ReportActuatorState("AA:BB:CC:DD:EE:FF", new()));
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(async () => await hub.ReportCommandAck(new()));
    }

    [Fact]
    public async Task Operator_CannotSubscribeToAnotherSitesDeviceOrStructure()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        SmartHydroAPI.Tests.TestSupport.TestData.SeedSites(db);
        db.Devices.Add(new SmartHydroAPI.Models.Entities.Device { MacAddress = "AA:BB:CC:DD:EE:99", StructureId = SmartHydroAPI.Tests.TestSupport.TestData.OtherTowerId });
        await db.SaveChangesAsync();
        var caller = new Moq.Mock<Microsoft.AspNetCore.SignalR.HubCallerContext>();
        caller.SetupGet(x => x.User).Returns(SmartHydroAPI.Tests.TestSupport.TestData.Operator());
        var hub = new SmartHydroAPI.Hubs.ActuatorHub(
            Moq.Mock.Of<SmartHydroAPI.Patterns.Decorator.IActuatorService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartHydroAPI.Hubs.ActuatorHub>.Instance,
            db, Moq.Mock.Of<Microsoft.AspNetCore.SignalR.IHubContext<SmartHydroAPI.Hubs.ActuatorHub>>()) { Context = caller.Object };
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => hub.RegisterDevice("AABBCCDDEE99"));
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => hub.JoinStructureGroup(SmartHydroAPI.Tests.TestSupport.TestData.OtherTowerId));
    }
}
