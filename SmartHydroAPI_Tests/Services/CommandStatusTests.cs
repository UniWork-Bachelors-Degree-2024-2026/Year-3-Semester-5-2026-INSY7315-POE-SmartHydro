using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using SmartHydroAPI.Controllers;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;
using System.Text.Json;

namespace SmartHydroAPI_Tests.Services;
public class CommandStatusTests
{
    [Theory]
    [InlineData("queued", false, false)]
    [InlineData("acked", true, true)]
    [InlineData("expired", true, false)]
    public async Task Polling_RecoversThePersistedCommandWithoutExecutingIt(string stage, bool terminal, bool ok)
    {
        using var db = TestData.CreateDatabase(); const string mac = "AA:BB:CC:DD:EE:91";
        db.Devices.Add(new Device { MacAddress = mac }); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db); var command = await queue.EnqueueAsync(mac, "gl", new() { ["gl"] = 1 }, "Test");
        if (stage == "acked") await queue.AcknowledgeAsync(mac, command.CommandId, 1, 2, "executed", "accepted");
        if (stage == "expired") { command.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var controller = new ActuatorsController(service, Mock.Of<IHubContext<ActuatorHub>>(), db) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = TestData.Admin() } } };
        var result = Assert.IsType<OkObjectResult>(await controller.CommandStatus(mac, command.CommandId, default));
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(terminal, json.GetProperty("terminal").GetBoolean()); Assert.Equal(ok, json.GetProperty("ok").GetBoolean());
        Assert.Equal(command.CommandId, json.GetProperty("commandId").GetInt32());
        Assert.Single(db.DeviceCommands); Assert.Empty(db.ActuatorHistory);
    }
    [Fact]
    public async Task Polling_DeniesAnotherSitesUser()
    {
        using var db = TestData.CreateDatabase(); TestData.SeedSites(db);
        db.Devices.Add(new Device { MacAddress = "AA:BB:CC:DD:EE:92", StructureId = TestData.OtherTowerId }); await db.SaveChangesAsync();
        var controller = new ActuatorsController(Mock.Of<IActuatorService>(), Mock.Of<IHubContext<ActuatorHub>>(), db) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = TestData.Operator() } } };
        Assert.IsType<NotFoundObjectResult>(await controller.CommandStatus("AA:BB:CC:DD:EE:92", 1, default));
    }
}
