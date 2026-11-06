using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SmartHydroAPI.Models.Dtos;
using Xunit;

namespace SmartHydroAPI_Tests.Controllers;

public class ConnectivityTelemetryTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 0, 1)]
    public void BothTransportsPreserveSelectedAndActiveModes(int active, int preferred, int reason)
    {
        var compact = JsonSerializer.Deserialize<LoraCompactTelemetryDto>(
            $$"""{"m":"AABBCCDDEEFF","md":{{active}},"pm":{{preferred}},"mr":{{reason}}}""")!;
        var direct = JsonSerializer.Deserialize<TelemetryIngestDto>(
            $$"""{"DeviceMac":"AA:BB:CC:DD:EE:FF","OperatingMode":{{active}},"PreferredMode":{{preferred}},"ModeReason":{{reason}}}""")!;
        Assert.Equal(active, compact.OperatingMode);
        Assert.Equal(preferred, compact.PreferredMode);
        Assert.Equal(reason, compact.ModeReason);
        Assert.Equal(compact.OperatingMode, direct.OperatingMode);
        Assert.Equal(compact.PreferredMode, direct.PreferredMode);
        Assert.Equal(compact.ModeReason, direct.ModeReason);
        Assert.True(Validator.TryValidateObject(direct, new ValidationContext(direct), [], true));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(1, 3, 0)]
    [InlineData(1, 0, 2)]
    public void InvalidModeStateIsRejected(int active, int preferred, int reason)
    {
        var dto = new TelemetryIngestDto { DeviceMac = "AA:BB:CC:DD:EE:FF",
            OperatingMode = active, PreferredMode = preferred, ModeReason = reason };
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }
}
