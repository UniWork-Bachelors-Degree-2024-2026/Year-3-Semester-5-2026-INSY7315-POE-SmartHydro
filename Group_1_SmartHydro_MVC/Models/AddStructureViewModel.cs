using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Group_1_SmartHydro_MVC.Models
{
    public class LabelColourOption
    {
        public LabelColourOption(string key, string name, string hex)
        {
            Key = key;
            Name = name;
            Hex = hex;
        }

        public string Key { get; }
        public string Name { get; }
        public string Hex { get; }
    }

    public static class StructureLabelColours
    {
        public const string Default = "green";

        private const string ApiDefaultHex = "#2ECC71";

        public static readonly IReadOnlyList<LabelColourOption> All = new List<LabelColourOption>
        {
            new("green", "Green", "#387544"),
            new("red", "Red", "#C24445"),
            new("amber", "Amber", "#D89538"),
            new("blue", "Blue", "#64A1BD"),
            new("brown", "Brown", "#93775A")
        };

        // Checks whether the key matches one of the available label colours, rejecting tampered form values
        public static bool IsValid(string? key)
        {
            return All.Any(c => c.Key == key);
        }

        // Converts a colour key into the hex value stored by the API, falling back to the default colour
        public static string ToApi(string? key)
        {
            var option = All.FirstOrDefault(c => c.Key == key);

            if (option == null)
                option = All.First(c => c.Key == Default);

            return option.Hex;
        }

        // Converts a stored API colour (a hex value or a legacy key) back into a colour key
        public static string FromApi(string? value)
        {
            var cleaned = (value ?? "").Trim();

            var option = All.FirstOrDefault(c =>
                string.Equals(c.Hex, cleaned, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Key, cleaned, StringComparison.OrdinalIgnoreCase));

            if (option == null || string.Equals(cleaned, ApiDefaultHex, StringComparison.OrdinalIgnoreCase))
                return Default;

            return option.Key;
        }
    }

    public static class MacAddressFormat
    {
        public const string Pattern = @"^\s*[0-9A-Fa-f]{2}([:-]?[0-9A-Fa-f]{2}){5}\s*$";

        // Checks the value is a valid MAC address and returns it upper case and colon separated
        public static bool TryNormalise(string? value, out string normalised)
        {
            normalised = "";
            if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, Pattern))
            {
                return false;
            }

            var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            var pairs = Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2));
            normalised = string.Join(":", pairs);
            return true;
        }
    }

    public class AddStructureViewModel
    {
        [BindNever]
        public string? Id { get; set; }

        [BindNever]
        public DateTime? CreatedAt { get; set; }

        public bool IsEdit
        {
            get { return !string.IsNullOrEmpty(Id); }
        }

        [Required(ErrorMessage = "Please enter the MAC address of the structure's controller.")]
        [RegularExpression(MacAddressFormat.Pattern, ErrorMessage = "Enter a valid MAC address, e.g. A4:CF:12:9B:3E:01.")]
        [Display(Name = "Controller MAC Address")]
        public string? MacAddress { get; set; }

        // The ESP32-CAM fitted to this structure.
        [RegularExpression(MacAddressFormat.Pattern, ErrorMessage = "Enter a valid camera MAC address, e.g. A4:CF:12:9B:3E:02.")]
        [Display(Name = "Camera MAC Address")]
        public string? CameraMac { get; set; }

        [Required(ErrorMessage = "Please enter a name for the structure.")]
        [StringLength(60, MinimumLength = 2, ErrorMessage = "The name must be between 2 and 60 characters.")]
        [Display(Name = "Structure Name")]
        public string? Name { get; set; }

        [EnumDataType(typeof(StructureType), ErrorMessage = "Please choose a structure type.")]
        public StructureType Type { get; set; } = StructureType.Hydroponic;

        public string? LabelColour { get; set; } = StructureLabelColours.Default;

        public string CreatedOn
        {
            get
            {
                var date = DateTime.Now;
                if (CreatedAt.HasValue)
                {
                    date = CreatedAt.Value.ToLocalTime();
                }

                return date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            }
        }
    }
}