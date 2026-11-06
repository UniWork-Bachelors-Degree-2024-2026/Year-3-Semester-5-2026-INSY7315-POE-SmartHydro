using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    // A safe range for one reading on one structure. Only ranges someone has changed are stored; a structure without a row for a reading uses the healthy default from ThresholdDefaults
    [Table("thresholds")]
    public class Threshold
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        // The structure this range belongs to. A structure belongs to exactly one site, so a range can never reach another site's structures
        [Column("structure_id")]
        public int StructureId { get; set; }

        [Required]
        [Column("structure_type")]
        [MaxLength(20)]
        public string StructureType { get; set; } = "VERTICAL_TOWER"; // 'VERTICAL_TOWER' or 'FODDER_TRAY'

        [Required]
        [Column("sensor_type")]
        [MaxLength(30)]
        public string SensorType { get; set; } = "PH"; // 'PH', 'EC', 'TEMPERATURE', 'HUMIDITY'

        [Column("min_value")]
        public decimal MinValue { get; set; }

        [Column("max_value")]
        public decimal MaxValue { get; set; }
    }
}
