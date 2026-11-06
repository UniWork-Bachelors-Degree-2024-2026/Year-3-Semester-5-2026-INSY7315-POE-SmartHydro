using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Patterns.Factory;

namespace SmartHydroAPI.Tests.Patterns
{
    public class AlertFactoryTests
    {
        private readonly AlertFactory _factory = new();

        // A new alert should copy the dto, upper-case component and severity, and start unresolved
        [Fact]
        public void CreateAlert_CopiesDtoAndNormalisesCase()
        {
            var dto = new CreateAlertDto
            {
                StructureId = 4,
                DeviceMac = "AA:BB:CC:DD:EE:FF",
                Component = "ph_sensor",
                Severity = "critical",
                Value = "4.1",
                Detail = "pH too low"
            };

            var alert = _factory.CreateAlert(dto);

            Assert.Equal(4, alert.StructureId);
            Assert.Equal("AA:BB:CC:DD:EE:FF", alert.DeviceMac);
            Assert.Equal("PH_SENSOR", alert.Component);
            Assert.Equal("CRITICAL", alert.Severity);
            Assert.Equal("4.1", alert.Value);
            Assert.Equal("pH too low", alert.Detail);
            Assert.False(alert.IsResolved);
        }

        // A dto with no detail should get the default anomaly message
        [Fact]
        public void CreateAlert_MissingDetail_UsesDefaultMessage()
        {
            var dto = new CreateAlertDto { StructureId = 1, Component = "EC_SENSOR", Detail = null };

            var alert = _factory.CreateAlert(dto);

            Assert.Equal("Automated threshold anomaly detected.", alert.Detail);
        }

        // The occurred time should be stamped in UTC at the moment the alert is made
        [Fact]
        public void CreateAlert_StampsCurrentUtcTime()
        {
            var before = DateTime.UtcNow;

            var alert = _factory.CreateAlert(new CreateAlertDto { Component = "SYSTEM" });

            var after = DateTime.UtcNow;
            Assert.Equal(DateTimeKind.Utc, alert.OccurredAt.Kind);
            Assert.InRange(alert.OccurredAt, before, after);
        }
    }
}