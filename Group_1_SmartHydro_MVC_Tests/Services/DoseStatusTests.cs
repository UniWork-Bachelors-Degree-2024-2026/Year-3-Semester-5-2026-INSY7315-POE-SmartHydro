using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Group_1_SmartHydro_MVC.Tests.TestSupport;

namespace Group_1_SmartHydro_MVC_Tests.Services;

public class DoseStatusTests
{
    [Fact]
    public async Task ReviewUsesSeparateEndpointAndPreservesRefusal()
    {
        await using var api = await FakeApiServer.StartAsync();
        api.StatusCode = 400;
        api.ResponseBody = """{"message":"The earlier request is still within its possible run or waiting period."}""";
        var id = Guid.NewGuid();
        var outcome = await new TelemetryApiClient(api.CreateClient()).ReviewUnknownDoseAsync("FC012CEE29F0", id,
            true, true, "Checked pumps and reservoir", "test-token", default);
        Assert.False(outcome.Ok);
        Assert.Contains("waiting period", outcome.Message);
        Assert.Equal("POST", api.LastMethod);
        using var payload = System.Text.Json.JsonDocument.Parse(api.LastBody!);
        Assert.True(payload.RootElement.GetProperty("pumpsCheckedOff").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("reservoirChecked").GetBoolean());
        Assert.False(payload.RootElement.TryGetProperty("dosingType", out _));
    }

    [Fact]
    public void ReviewedDoseDoesNotClaimCompletionOrKeepAnUnknownOutcomeBlock()
    {
        var dose = new DoseRecord("PH_UP", 2m, "FAILED", DateTime.UtcNow.AddHours(-1), null, "User",
            Guid.NewGuid(), 42, "REVIEWED", "operator_reviewed_unknown");
        var summary = SensorBindings.SummariseDoses(new[] { dose }, DosingCatalog.PhGroup);
        Assert.False(summary.HasPending);
        Assert.Equal("REVIEWED", summary.Status);
        Assert.Contains("delivered amount unknown", summary.Last);
        Assert.DoesNotContain("completed", summary.Last);
    }

    [Fact]
    public void UnresolvedDose_BlocksBothGroupsAndIsNotHiddenByNewerFailure()
    {
        var unresolved = new DoseRecord("PH_DOWN", 2m, "UNCONFIRMED", DateTime.UtcNow.AddDays(-2), null, "User", Guid.NewGuid(), 42, "UNCONFIRMED", "completion_unconfirmed");
        var failed = new DoseRecord("NUTRIENT_UP", 20m, "FAILED", DateTime.UtcNow, null, "User", Guid.NewGuid(), 43, "REJECTED", "sensor_fault");
        var summary = SensorBindings.SummariseDoses(new[] { unresolved, failed }, DosingCatalog.NutrientGroup);
        Assert.False(summary.HasPending);
        Assert.Equal("UNCONFIRMED", summary.Status);
        Assert.Equal(unresolved.Id, summary.DoseId);
        Assert.Contains("Dosing blocked", summary.Pending);
        var completed = unresolved with { Status = "COMPLETED", DeliveryStatus = "COMPLETED" };
        Assert.Equal("COMPLETED", SensorBindings.SummariseDoses(new[] { completed, failed }, DosingCatalog.NutrientGroup, unresolved.Id).Status);
    }
    [Theory]
    [InlineData("PH_UP", 2)]
    [InlineData("PH_DOWN", 2)]
    [InlineData("NUTRIENT_UP", 20)]
    [InlineData("DILUTION_DOWN", 25)]
    public async Task DoseSubmission_PreservesApiRefusalAndCanonicalPayload(string type, int ml)
    {
        await using var api = await FakeApiServer.StartAsync();
        api.StatusCode = 400;
        api.ResponseBody = """{"title":"Invalid Dosing Target","detail":"A previous dose has no confirmed outcome. Check the controller before requesting another dose."}""";
        var outcome = await new TelemetryApiClient(api.CreateClient()).SendDoseAsync("FC012CEE29F0", type, ml, "test-token");
        Assert.False(outcome.Ok);
        Assert.Equal(400, outcome.StatusCode);
        Assert.Contains("previous dose has no confirmed outcome", outcome.Message);
        Assert.Equal("POST", api.LastMethod);
        using var payload = System.Text.Json.JsonDocument.Parse(api.LastBody!);
        Assert.Equal(type, payload.RootElement.GetProperty("dosingType").GetString());
        Assert.Equal(ml, payload.RootElement.GetProperty("targetVolumeMl").GetDecimal());
        Assert.Equal("FC:01:2C:EE:29:F0", payload.RootElement.GetProperty("deviceMac").GetString());
    }

    [Theory]
    [InlineData("QUEUED", true, "Queued")]
    [InlineData("SENT", true, "Sent")]
    [InlineData("RUNNING", true, "running")]
    [InlineData("AWAITING_COMPLETION", true, "completion")]
    [InlineData("REJECTED", false, "refused")]
    [InlineData("TIMED_OUT", false, "timed out")]
    [InlineData("UNCONFIRMED", false, "not confirmed")]
    [InlineData("COMPLETED", false, "completed")]
    [InlineData("CANCELLED", false, "cancelled")]
    public void Summary_ShowsTheActualDeliveryStage(string status, bool waiting, string words)
    {
        var dose = new DoseRecord("PH_DOWN", 1m, "PENDING", DateTime.UtcNow, null, "User",
            Guid.NewGuid(), 42, status, "safety_blocked");
        var summary = SensorBindings.SummariseDoses(new[] { dose }, DosingCatalog.PhGroup, dose.Id);
        Assert.Equal(waiting, summary.HasPending);
        Assert.Equal(status, summary.Status);
        Assert.Contains(words, waiting ? summary.Pending : summary.Last);
        Assert.Contains("estimated", waiting ? summary.Pending : summary.Last);
    }

    [Fact]
    public void MissingRequestedRecord_IsNotMistakenForAnEmptyQueue()
    {
        var summary = SensorBindings.SummariseDoses(Array.Empty<DoseRecord>(), DosingCatalog.PhGroup, Guid.NewGuid());
        Assert.True(summary.HasPending); Assert.Equal("UNKNOWN", summary.Status);
    }

    [Theory]
    [InlineData(200, "[]", true)]
    [InlineData(503, "{}", false)]
    [InlineData(200, "invalid-json", false)]
    public async Task History_DistinguishesNoDosesFromAnUnavailableApi(int status, string body, bool available)
    {
        await using var api = await FakeApiServer.StartAsync(); api.StatusCode = status; api.ResponseBody = body;
        var result = await new TelemetryApiClient(api.CreateClient()).ReadDosingHistoryAsync("AA:BB:CC:DD:EE:C1", "token");
        Assert.Equal(available, result.Available);
    }

    [Fact]
    public void FixedButtons_StayWithinTheAgreedCommandLimit()
    {
        foreach (var type in new[] { "PH_UP", "PH_DOWN", "NUTRIENT_UP", "DILUTION_DOWN" })
            Assert.InRange(DosingCatalog.Find(type)!.VolumeMl, 0.5m, 25m);
    }
}
