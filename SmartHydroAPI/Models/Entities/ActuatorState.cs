using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("actuator_states")]
    public class ActuatorState
    {
        [Key]
        [Column("device_mac")]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        [Column("grow_light")]
        public bool GrowLight { get; set; }

        [Column("circulation_fan")]
        public bool CirculationFan { get; set; }

        [Column("main_water_pump")]
        public bool MainWaterPump { get; set; }

        [Column("extractor_fan")]
        public bool ExtractorFan { get; set; }

        [Column("ph_up_pump")]
        public bool PhUpPump { get; set; }

        [Column("ph_down_pump")]
        public bool PhDownPump { get; set; }

        [Column("nutrient_up_pump")]
        public bool NutrientUpPump { get; set; }

        [Column("dilution_down_pump")]
        public bool DilutionDownPump { get; set; }

        [Column("spray_system")]
        public bool SpraySystem { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("reported_sequence")]
        public long? ReportedSequence { get; set; }

        // Navigation
        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
