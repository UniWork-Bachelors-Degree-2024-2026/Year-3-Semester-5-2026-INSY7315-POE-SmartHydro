using System.Globalization;

namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// Represents a single photo shown in the gallery. Holds the photo's details (structure,
    /// plant, camera, time taken, favourite state and image URL) and works out a display title.
    /// </summary>
    public class CaptureViewModel
    {
        public string Id { get; set; } = "";
        public string Structure { get; set; } = "";
        public string Plant { get; set; } = "";
        public string Camera { get; set; } = "";
        public DateTime TakenAt { get; set; }
        public bool IsFavourite { get; set; }
        public string ImageUrl { get; set; } = "";

        public string Label { get; set; } = "";

        /// <summary>
        /// Gets the title shown for the photo. Combines the structure and plant (for example
        /// "Tunnel A - Lettuce") when available, otherwise uses the photo's label, and falls
        /// back to "Photo" if neither exists.
        /// </summary>
        public string Title
        {
            get
            {
                var parts = new List<string>();

                if (Structure != "")
                {
                    parts.Add(Structure);
                }

                if (Plant != "")
                {
                    parts.Add(Plant);
                }

                if (parts.Count > 0)
                {
                    return string.Join(" - ", parts);
                }

                if (Label != "")
                {
                    return Label;
                }

                return "Photo";
            }
        }
    }

    /// <summary>
    /// The view model for the photo gallery page. Holds all captures, today's date and the
    /// structure names for the upload form. Provides the filter option lists (plants,
    /// structures and cameras) and helper methods for formatting dates and building the
    /// search text for each photo.
    /// </summary>
    public class GalleryViewModel
    {
        public DateTime Today { get; set; }
        public List<CaptureViewModel> Captures { get; set; } = new();

        public List<string> UploadStructures { get; set; } = new();

        /// <summary>
        /// The cameras fitted to the current site's structures, one per structure that has a camera.
        /// </summary>
        public List<BoardCameraOptionViewModel> BoardCameras { get; set; } = new();

        /// <summary>
        /// True when at least one of the current site's structures has a camera. The "Live camera"
        /// option in "Add a photo" is only shown when this is true.
        /// </summary>
        public bool HasBoardCamera
        {
            get { return BoardCameras.Count > 0; }
        }

        /// <summary>
        /// Gets today's date in yyyy-MM-dd format, used by the view for date filtering
        /// and as the default value of the date input.
        /// </summary>
        public string TodayKey
        {
            get { return Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets a sorted list of unique plant names from the captures, ignoring blanks.
        /// Used to fill the plant filter.
        /// </summary>
        public List<string> Plants
        {
            get { return Captures.Select(c => c.Plant).Where(p => p != "").Distinct().OrderBy(p => p).ToList(); }
        }

        /// <summary>
        /// Gets a sorted list of unique structure names from the captures, ignoring blanks.
        /// Used to fill the structure filter.
        /// </summary>
        public List<string> Structures
        {
            get { return Captures.Select(c => c.Structure).Where(s => s != "").Distinct().OrderBy(s => s).ToList(); }
        }

        /// <summary>
        /// Gets a sorted list of unique camera names from the captures, ignoring blanks.
        /// Used to fill the camera filter.
        /// </summary>
        public List<string> Cameras
        {
            get { return Captures.Select(c => c.Camera).Where(c => c != "").Distinct().OrderBy(c => c).ToList(); }
        }

        /// <summary>
        /// Returns the date a photo was taken in yyyy-MM-dd format, so the view can group
        /// or filter photos by date.
        /// </summary>
        /// <param name="capture">The photo to format.</param>
        /// <returns>The date key, for example "2026-09-22".</returns>
        public string DateKey(CaptureViewModel capture)
        {
            return capture.TakenAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns a short, friendly label for when a photo was taken. Shows "Today",
        /// "Yesterday" or a short date, followed by the time (for example "Today - 14:30").
        /// </summary>
        /// <param name="capture">The photo to describe.</param>
        /// <returns>A short date and time label.</returns>
        public string WhenLabel(CaptureViewModel capture)
        {
            var day = capture.TakenAt.Date;
            string label;

            if (day == Today.Date)
            {
                label = "Today";
            }
            else if (day == Today.Date.AddDays(-1))
            {
                label = "Yesterday";
            }
            else
            {
                label = day.ToString("d MMM", CultureInfo.InvariantCulture);
            }

            return label + " - " + capture.TakenAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the full date and time a photo was taken, for example
        /// "Tuesday, 22 September 2026 at 14:30". Used where more detail is needed.
        /// </summary>
        /// <param name="capture">The photo to describe.</param>
        /// <returns>A full date and time label.</returns>
        public string FullLabel(CaptureViewModel capture)
        {
            return capture.TakenAt.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture)
                + " at " + capture.TakenAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds a lowercase block of searchable text for a photo so the gallery search can
        /// match it. Includes the title, plant, structure, camera and several date formats,
        /// and adds "today", "yesterday" or "favourite favorite" (both spellings) where they apply.
        /// </summary>
        /// <param name="capture">The photo to build search text for.</param>
        /// <returns>A lowercase string of search keywords.</returns>
        public string SearchText(CaptureViewModel capture)
        {
            var parts = new List<string>
            {
                capture.Title,
                capture.Plant,
                capture.Structure,
                capture.Camera,
                capture.TakenAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                capture.TakenAt.ToString("dddd MMM HH:mm", CultureInfo.InvariantCulture)
            };

            if (capture.TakenAt.Date == Today.Date)
            {
                parts.Add("today");
            }

            if (capture.TakenAt.Date == Today.Date.AddDays(-1))
            {
                parts.Add("yesterday");
            }

            if (capture.IsFavourite)
            {
                parts.Add("favourite favorite");
            }

            return string.Join(" ", parts).ToLowerInvariant();
        }
    }
}