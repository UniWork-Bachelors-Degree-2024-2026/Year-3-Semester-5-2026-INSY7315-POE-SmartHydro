using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Tests.Services
{
    public class ThresholdDefaultsTests
    {
        // Fodder uses the same four sensor types, with its own EC and climate defaults
        [Fact]
        public void For_FodderTray_ReturnsFodderRangesIncludingPh()
        {
            var ranges = ThresholdDefaults.For("FODDER_TRAY");

            Assert.Equal(4, ranges.Count);
            Assert.Contains(ranges, r => r.SensorType == "PH");
        }

        // The fodder type should be matched regardless of case and surrounding spaces
        [Fact]
        public void For_FodderTrayWithOddCasing_IsStillRecognised()
        {
            var ranges = ThresholdDefaults.For("  fodder_tray ");

            Assert.Contains(ranges, r => r.SensorType == "PH");
        }

        // Unknown, blank or missing structure types should fall back to the hydroponic tower ranges
        [Theory]
        [InlineData("VERTICAL_TOWER")]
        [InlineData("SOMETHING_ELSE")]
        [InlineData("")]
        [InlineData(null)]
        public void For_OtherTypes_ReturnsHydroponicRanges(string? structureType)
        {
            var ranges = ThresholdDefaults.For(structureType);

            Assert.Equal(4, ranges.Count);
            Assert.Contains(ranges, r => r.SensorType == "PH" && r.Min == 5.5m && r.Max == 6.5m);
        }

        // With nothing saved, every reading should get its healthy default with id 0 and the given structure id
        [Fact]
        public void Resolve_NothingSaved_ReturnsAllDefaults()
        {
            var resolved = ThresholdDefaults.Resolve("VERTICAL_TOWER", 12, new List<Threshold>());

            Assert.Equal(4, resolved.Count);
            Assert.All(resolved, t => Assert.Equal(0, t.Id));
            Assert.All(resolved, t => Assert.Equal(12, t.StructureId));
        }

        // A saved range should replace only its own default and leave the others untouched
        [Fact]
        public void Resolve_OneSaved_ReplacesOnlyThatReading()
        {
            var saved = new List<Threshold>
            {
                new Threshold { Id = 99, StructureId = 12, SensorType = "ec", MinValue = 1.2m, MaxValue = 2.0m }
            };

            var resolved = ThresholdDefaults.Resolve("VERTICAL_TOWER", 12, saved);

            var ec = Assert.Single(resolved, t => t.SensorType.Equals("EC", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(99, ec.Id);
            Assert.Equal(1.2m, ec.MinValue);
            Assert.Equal(3, resolved.Count(t => t.Id == 0));
        }

        // A saved fodder pH range must survive resolution
        [Fact]
        public void Resolve_SavedPhOnFodderTray_IsPreserved()
        {
            var saved = new List<Threshold>
            {
                new Threshold { Id = 5, SensorType = "PH", MinValue = 1m, MaxValue = 2m }
            };

            var resolved = ThresholdDefaults.Resolve("FODDER_TRAY", 3, saved);

            Assert.Equal(5, Assert.Single(resolved, t => t.SensorType == "PH").Id);
        }

        // A blank structure type should be stored on the defaults as VERTICAL_TOWER
        [Fact]
        public void Resolve_BlankType_LabelsDefaultsAsVerticalTower()
        {
            var resolved = ThresholdDefaults.Resolve(" ", 1, new List<Threshold>());

            Assert.All(resolved, t => Assert.Equal("VERTICAL_TOWER", t.StructureType));
        }
    }
}