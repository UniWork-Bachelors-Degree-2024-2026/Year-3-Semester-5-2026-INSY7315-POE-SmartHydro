using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC.Patterns.Factory
{
    public interface IDashboardBuilder
    {
        StructureType Type { get; }

        DashboardViewModel Build(StructureReadings readings);
    }

    public interface IDashboardFactory
    {
        DashboardViewModel Create(StructureViewModel structure, StructureReadings readings);
    }

    public sealed class DashboardFactory : IDashboardFactory
    {
        private readonly Dictionary<StructureType, IDashboardBuilder> _builders;

        public DashboardFactory(IEnumerable<IDashboardBuilder> builders)
        {
            _builders = builders.ToDictionary(b => b.Type);
        }

        // Picks the builder registered for the structure's type, builds its dashboard from the readings and fills in the structure's live status
        public DashboardViewModel Create(StructureViewModel structure, StructureReadings readings)
        {
            if (!_builders.TryGetValue(structure.Type, out var builder))
            {
                throw new InvalidOperationException($"No dashboard builder is registered for {structure.Type} structures.");
            }

            var model = builder.Build(readings);
            model.Structure = structure;
            model.IsOnline = structure.IsOnline;

            if (structure.IsOnline)
            {
                var lastReading = SensorBindings.FormatTime(readings.RecordedAt);

                if (lastReading.Length > 0)
                {
                    model.StatusSubtitle = "Last reading " + lastReading;
                }
                else
                {
                    model.StatusSubtitle = "Online. Waiting for the first reading.";
                }
            }

            if (!structure.IsOnline)
            {
                model.StatusSubtitle = structure.StatusText;

                if (!string.IsNullOrWhiteSpace(structure.StatusNote))
                {
                    model.StatusSubtitle = structure.StatusText + " - " + structure.StatusNote;
                }

                // When physical controller is offline or unplugged, hardware relays are physically de-energized
                foreach (var card in model.Cards)
                {
                    card.ControlOn = false;
                }
            }

            return model;
        }
    }
}