using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
namespace Group_1_SmartHydro_MVC_Tests.Services;
public class DosingActivityTests
{
    [Theory]
    [InlineData("ON")]
    [InlineData("OFF")]
    public void RecordedPumpDetailsExplainCodesInPlainLanguage(string state)
    {
        var paragraphs = SensorActivityText.DescriptionParagraphs($"main_water_pump reported {state} | ML class 2 | GPIO_{state} | local action; no cloud CID");
        Assert.Equal($"Water pump was reported {state} by the controller.", paragraphs[0]);
        Assert.Contains("does not confirm equipment operation", paragraphs[1]);
        Assert.Contains("Equipment power was not measured", paragraphs[2]);
        Assert.Contains("not linked to a cloud command", paragraphs[3]);
        Assert.DoesNotContain(paragraphs, p => p.Contains("GPIO_") || p.Contains("ML class") || p.Contains("CID"));
    }
    [Fact]
    public void ActivityDetailsUseReadableParagraphsWithoutChangingMeasurements()
    {
        var paragraphs = SensorActivityText.DescriptionParagraphs(" Reading 0.18 mS/cm. | Estimated 5 mL = 4000 ms / 800 ms/mL.\r\n | No dose was started. ");
        Assert.Equal(new[] { "Reading 0.18 mS/cm.", "Estimated 5 mL = 4000 ms / 800 ms/mL.", "No dose was started." }, paragraphs);
        Assert.Empty(SensorActivityText.DescriptionParagraphs(" | \r\n "));
        Assert.Equal("<script>recorded text</script>", Assert.Single(SensorActivityText.DescriptionParagraphs("<script>recorded text</script>")));
    }
    [Fact]
    public void TechnicalDetailsArePreservedAndUnknownSourcesUsePlainLabels()
    {
        var entry = SensorActivityText.FromAudit(new() { Action = "PH_DOWN", Status = "REFUSED",
            Source = "raw firmware source", Details = "calculation-details | sensor_fault" });
        Assert.Equal("calculation-details | sensor_fault", entry.Description);
        Assert.Equal("Controller", entry.Source);
        Assert.Contains("Check the sensor", entry.Summary);
    }
    [Theory]
    [InlineData("sensor_stale", "out of date")]
    [InlineData("sensor_recovering", "stable sensor readings")]
    public void SafetyRefusalExplainsWhyValidLookingReadingsMayStillBlockDosing(string reason, string explanation)
    {
        var entry = SensorActivityText.FromAudit(new() { Action = "PH_UP", Status = "REFUSED", Details = "dose estimate | " + reason });
        Assert.Contains(explanation, entry.Summary);
        Assert.Contains("Dose not started", entry.Summary);
        Assert.True(entry.IsWarning);
    }

    [Theory]
    [InlineData("WaterPump", "wp", "Water pump")]
    [InlineData("CleanWater", "ph", "pH pumps")]
    [InlineData("Nutrients", "ec", "Nutrient pumps")]
    [InlineData("Temperature", "fc", "Circulation fan")]
    [InlineData("Humidity", "fe", "Extractor fan")]
    [InlineData("Sunlight", "gl", "Grow lights")]
    public void CommandActivityAppearsOnItsSensorPage(string page, string action, string title)
    {
        var model = new SensorDetailViewModel();
        SensorBindings.FillSensor(model, SensorBindings.For(page), new StructureReadings {
            Audit = new() { new AuditRecord { Action = action, Status = "QUEUED", Source = "User - MVC" },
                new AuditRecord { Action = "other", Status = "QUEUED" } }
        }, true);
        var entry = Assert.Single(model.ActivityLog);
        Assert.Equal(title, entry.Title);
        Assert.Equal("Dashboard request", entry.Source);
        Assert.Contains("Waiting for a controller reply", entry.Summary);
    }

    [Fact]
    public void UnknownSwitchOutcomeDoesNotClaimADosingLockout()
    {
        var entry = SensorActivityText.FromAudit(new() { Action = "gl", Status = "UNCONFIRMED" });
        Assert.Contains("Please check the equipment", entry.Summary);
        Assert.DoesNotContain("dosing is blocked", entry.Summary);
    }

    [Fact]
    public void MixingWaitDoesNotClaimCirculationIsRunning()
    {
        var entry = SensorActivityText.FromAudit(new() { Action = "PH_UP", Status = "REFUSED", Details = "mixing_lockout" });
        Assert.Contains("waiting period", entry.Summary);
        Assert.Contains("does not confirm water circulation", entry.Summary);
    }

    [Fact]
    public void RefusalExplainsRecordedReason_WithoutUsingTodaysReading()
    {
        var model = new SensorDetailViewModel();
        SensorBindings.FillSensor(model, SensorBindings.For("Nutrients"), new StructureReadings {
            HasTelemetry = true, EcMs = 1.9m,
            Audit = new() { new AuditRecord { Action = "NUTRIENT_UP", Status = "REFUSED", TriggerValue = .31m,
                Minimum = 1m, Maximum = 2.5m, Details = "formula and estimates | sensor_fault", CommandId = 42 } }
        }, true);
        var entry = Assert.Single(model.ActivityLog);
        Assert.Contains("sensor reading is unavailable", entry.Summary);
        Assert.Contains("310 µS/cm", entry.Summary);
        Assert.Contains("1000–2500 µS/cm", entry.Summary);
        Assert.DoesNotContain("1900", entry.Summary);
        Assert.Contains("Command 42", entry.Description);
        Assert.True(entry.IsWarning);
    }

    [Fact]
    public void SafetyWordsInsideFormulaAreNotTreatedAsTheRefusalReason()
    {
        var entry = SensorActivityText.FromAudit(new() { Action = "PH_DOWN", Status = "REFUSED",
            Details = "pulse estimate; mixing lockout 15m; pH hourly cap 10mL | unrecognised_reason" });
        Assert.Contains("recorded reason", entry.Summary);
        Assert.DoesNotContain("hourly pH dosing limit has been reached", entry.Summary);
    }

    [Fact]
    public void RecentRelayHistoryIsNotHiddenByOlderAuditEvents()
    {
        var now = DateTime.UtcNow;
        var model = new SensorDetailViewModel { ControlLabel = "Circulation fan" };
        SensorBindings.FillSensor(model, SensorBindings.For("Temperature"), new StructureReadings {
            Audit = Enumerable.Range(1, 8).Select(i => new AuditRecord { Action = "fc", Status = "QUEUED", RecordedAt = now.AddHours(-i) }).ToList(),
            Events = new() { new("circulation_fan", true, now, "Node ACK 12") }
        }, true);
        Assert.Equal(8, model.ActivityLog.Count);
        Assert.Contains("confirmed this relay state", model.ActivityLog[0].Summary);
    }

    [Fact]
    public void TimedHistoryDoesNotClaimTheEquipmentRanForTheRequestedDuration()
    {
        var model = new SensorDetailViewModel { ControlLabel = "Water pump" };
        SensorBindings.FillSensor(model, SensorBindings.For("WaterPump"), new StructureReadings {
            Events = new() { new("MainWaterPump_TIMED_8S", true, DateTime.UtcNow, "Operator") }
        }, true);
        var entry = Assert.Single(model.ActivityLog);
        Assert.Contains("does not confirm completion", entry.Summary);
        Assert.DoesNotContain("ran for", entry.Title);
    }

    [Fact]
    public void PhButtons_UseMicroDosesInsteadOfTheOldTenMlPulse() {
        Assert.Equal(2m, DosingCatalog.Find("PH_UP")!.VolumeMl);
        Assert.Equal(2m, DosingCatalog.Find("PH_DOWN")!.VolumeMl);
    }
    [Fact]
    public void StructuredActivity_ShowsItsSourceAndUncertainTimeWithoutInventingDelivery() {
        var model = new SensorDetailViewModel { ControlKey = "ph" };
        SensorBindings.FillSensor(model, SensorBindings.For("CleanWater"), new StructureReadings {
            Audit = new() { new AuditRecord { Source = "User - BLE", Action = "PH_DOWN", Status = "DOSE_COMPLETED",
                Details = "Estimated 2 mL; flow is uncalibrated.", CaptureTimeUncertain = true, ReceivedAt = DateTime.UtcNow } }
        }, true);
        var entry = Assert.Single(model.ActivityLog);
        Assert.Equal("Local Bluetooth", entry.Source); Assert.Contains("uncalibrated", entry.Description); Assert.Contains("uncertain", entry.Time);
        Assert.Contains("not measured", entry.Summary);
    }
    [Theory]
    [InlineData("PENDING", "TIMED_OUT")]
    [InlineData("DISPATCHED", "UNCONFIRMED")]
    public void OldLegacyDoses_DoNotKeepTheButtonsWaitingForever(string state, string expected) {
        var summary = SensorBindings.SummariseDoses(new[] { new DoseRecord("PH_DOWN", 10m, state, DateTime.UtcNow.AddDays(-2), null, "Legacy") }, DosingCatalog.PhGroup);
        Assert.False(summary.HasPending); Assert.Equal(expected, summary.Status);
    }
    [Fact]
    public void ExtremePhReport_IsFlaggedWithoutReplacingTheMeasurement() {
        var model = new SensorDetailViewModel();
        SensorBindings.FillSensor(model, SensorBindings.For("CleanWater"), new StructureReadings { HasTelemetry = true, Ph = .1m }, true);
        Assert.Equal("0.1", model.HeroValue); Assert.Contains("calibration", model.MeasurementNotice);
    }
}
