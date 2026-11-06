namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideNote
    {
        public string Kind { get; set; } = "info";

        public string Html { get; set; } = "";

        // Returns the Material Symbols icon that matches the kind of note.
        public string GetIcon()
        {
            if (Kind == "warn")
            {
                return "warning";
            }

            if (Kind == "stop")
            {
                return "error";
            }

            if (Kind == "good")
            {
                return "check_circle";
            }

            return "info";
        }

        // Returns the CSS classes that colour the note by its kind.
        public string GetCssClass()
        {
            return "sh-note sh-note-" + Kind;
        }
    }
}
