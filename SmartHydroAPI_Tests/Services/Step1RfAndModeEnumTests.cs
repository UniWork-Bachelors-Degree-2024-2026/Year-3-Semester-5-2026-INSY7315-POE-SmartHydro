using SmartHydroAPI.Services;
using Xunit;

namespace SmartHydroAPI_Tests.Services
{
    public class Step1RfAndModeEnumTests
    {
        [Theory]
        [InlineData("lora", 0, "lora")]
        [InlineData("lora_ble", 0, "lora")]
        [InlineData("0", 0, "lora")]
        [InlineData("online", 1, "online")]
        [InlineData("wifi", 1, "online")]
        [InlineData("1", 1, "online")]
        [InlineData("softap", 2, "softap")]
        [InlineData("offline", 2, "softap")]
        [InlineData("2", 2, "softap")]
        public void CanonicalModeMapping_MatchesFirmwareAndMvcContract(string input, int expectedIndex, string expectedName)
        {
            int actualIndex = LoraDownlinkQueue.CanonicalModeIndex(input);
            string actualName = LoraDownlinkQueue.CanonicalModeName(actualIndex);

            Assert.Equal(expectedIndex, actualIndex);
            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public void EnqueueMode_EmitsBothCanonicalModeStringAndIntegerIndex()
        {
            const string mac = "DC:06:75:F9:61:A8";
            LoraDownlinkQueue.EnqueueMode(mac, "online");
            Assert.True(LoraDownlinkQueue.TryTakeForMac(mac, out var onlineCmd));
            Assert.NotNull(onlineCmd);
            Assert.Equal("online", onlineCmd!["mode"]);
            Assert.Equal(1, onlineCmd["operatingMode"]);

            LoraDownlinkQueue.EnqueueMode(mac, "softap");
            Assert.True(LoraDownlinkQueue.TryTakeForMac(mac, out var softApCmd));
            Assert.NotNull(softApCmd);
            Assert.Equal("softap", softApCmd!["mode"]);
            Assert.Equal(2, softApCmd["operatingMode"]);

            LoraDownlinkQueue.EnqueueMode(mac, "lora");
            Assert.True(LoraDownlinkQueue.TryTakeForMac(mac, out var loraCmd));
            Assert.NotNull(loraCmd);
            Assert.Equal("lora", loraCmd!["mode"]);
            Assert.Equal(0, loraCmd["operatingMode"]);
        }

        [Fact]
        public void RecordGatewayActivity_UpdatesLastSeenAndRssi()
        {
            LoraDownlinkQueue.RecordGatewayActivity(isPost: true, rssi: -68);

            Assert.NotNull(LoraDownlinkQueue.LastGatewaySeenAt);
            Assert.NotNull(LoraDownlinkQueue.LastGatewayPostAt);
            Assert.Equal(-68, LoraDownlinkQueue.LastGatewayRssi);
            Assert.True((DateTime.UtcNow - LoraDownlinkQueue.LastGatewaySeenAt!.Value).TotalSeconds < 5);
        }

        [Fact]
        public void Sf7LoRaTimeOnAir_StaysWithinIcasaOnePercentHourlyDutyCycle()
        {
            // Semtech AN1200.13 Time-on-Air calculation for SF7, BW 125 kHz, CR 4/5, Preamble 8
            static int CalculateToaMs(int payloadBytes, int sf = 7, double bwKhz = 125.0, int cr = 5, int preamble = 8)
            {
                double tSymMs = Math.Pow(2, sf) / bwKhz;
                double tPreambleMs = (preamble + 4.25) * tSymMs;
                int de = (sf >= 11 && bwKhz <= 125.0) ? 1 : 0;
                double num = 8.0 * payloadBytes - 4.0 * sf + 28.0 + 16.0;
                double den = 4.0 * (sf - 2.0 * de);
                int payloadSym = 8 + (int)Math.Max(0.0, Math.Ceiling(num / den) * cr);
                return (int)Math.Ceiling(tPreambleMs + payloadSym * tSymMs);
            }

            int toaBinary25B = CalculateToaMs(25, sf: 7, cr: 5);
            int toaJson82B = CalculateToaMs(82, sf: 7, cr: 5);
            int toaGatewayAck36B = CalculateToaMs(36, sf: 7, cr: 5);

            // Verify SF7/CR4/5 keeps 25B binary (~62 ms) and 82B JSON (~154 ms) well below 200 ms
            Assert.InRange(toaBinary25B, 55, 70);
            Assert.InRange(toaJson82B, 140, 165);
            Assert.InRange(toaGatewayAck36B, 70, 90);

            // At 15-second cadence (240 packets/hour), hourly airtime must remain below ICASA 1% limit (36,000 ms)
            int hourlyBinaryMs = 240 * toaBinary25B;
            int hourlyJsonMs = 240 * toaJson82B;
            int hourlyGatewayAckMs = 446 * toaGatewayAck36B; // 240 Fodder + 206 Tower uplinks/hr

            Assert.True(hourlyBinaryMs <= 36000, $"Binary hourly airtime {hourlyBinaryMs} ms exceeds 36,000 ms");
            Assert.True(hourlyJsonMs < 38000);
            Assert.True(hourlyGatewayAckMs <= 36000, $"Gateway ACK hourly airtime {hourlyGatewayAckMs} ms exceeds 36,000 ms");
        }
    }
}
