using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC.Tests.Services
{
    public class ThresholdCatalogTests
    {
        // The page should offer exactly the hydroponic and fodder structure types
        [Fact]
        public void Groups_ContainsHydroponicAndFodder()
        {
            var apiTypes = ThresholdCatalog.Groups.Select(g => g.ApiType).ToList();

            Assert.Equal(new[] { "VERTICAL_TOWER", "FODDER_TRAY" }, apiTypes);
        }

        // Fodder trays offer a pH setting matching the updated canonical 6-card structure
        [Fact]
        public void FodderGroup_HasPh()
        {
            var fodder = ThresholdCatalog.ForApiType("FODDER_TRAY");

            Assert.NotNull(fodder);
            Assert.Contains(fodder.Sensors, s => s.Key == "PH");
        }

        // A reading should be found by its API name regardless of letter case
        [Theory]
        [InlineData("PH")]
        [InlineData("ph")]
        [InlineData("Temperature")]
        public void ForKey_KnownReading_IsFound(string key)
        {
            Assert.NotNull(ThresholdCatalog.ForKey(key));
        }

        // A reading with no threshold should not be found
        [Fact]
        public void ForKey_UnknownReading_ReturnsNull()
        {
            Assert.Null(ThresholdCatalog.ForKey("LIGHT"));
        }

        // EC is stored in mS/cm by the API but shown in uS/cm, so its scale must be 1000
        [Fact]
        public void ForKey_Ec_ConvertsByOneThousand()
        {
            var ec = ThresholdCatalog.ForKey("EC");

            Assert.NotNull(ec);
            Assert.Equal(1000m, ec.Scale);
        }

        // A structure type should be found ignoring spaces and letter case
        [Fact]
        public void ForApiType_IgnoresCaseAndSpaces()
        {
            var group = ThresholdCatalog.ForApiType("  vertical_tower ");

            Assert.NotNull(group);
            Assert.Equal("hydroponic", group.Key);
        }

        // A blank or unknown structure type should not be found
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("GREENHOUSE")]
        public void ForApiType_BlankOrUnknown_ReturnsNull(string? apiType)
        {
            Assert.Null(ThresholdCatalog.ForApiType(apiType));
        }

        // Every reading's lowest allowed value should be below its highest
        [Fact]
        public void AllSensors_HaveValidBounds()
        {
            var sensors = ThresholdCatalog.Groups.SelectMany(g => g.Sensors);

            Assert.All(sensors, s => Assert.True(s.Lowest < s.Highest));
        }
    }
}