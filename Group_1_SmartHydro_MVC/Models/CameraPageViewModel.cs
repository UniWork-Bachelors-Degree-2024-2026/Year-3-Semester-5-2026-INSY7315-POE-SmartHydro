namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// The view model shared by the two camera pages: "Device camera" (the camera on the phone or
    /// laptop being used) and "Board camera" (the ESP32-CAM connected to the SmartHydro board).
    /// Holds the name shown on the feed, the structure names that can be tagged on a photo, and
    /// the sensor status shown alongside the feed.
    /// </summary>
    public class CameraPageViewModel
    {
        /// <summary>The label shown in the corner of the feed, for example "DEVICE CAMERA" or "ESP32-CAM".</summary>
        public string CameraLabel { get; set; } = "";

        public List<string> Structures { get; set; } = new();

        /// <summary>
        /// Board camera only: the structure the camera is fitted to. It is chosen in the Structure
        /// field of "Save this photo" automatically.
        /// </summary>
        public string CameraStructure { get; set; } = "";

        /// <summary>
        /// Board camera only: the ID of the structure whose camera is shown. It is sent with every
        /// request so the right camera is switched on, watched and used for photos.
        /// </summary>
        public string CameraStructureId { get; set; } = "";

        /// <summary>
        /// Board camera only: the message shown when no camera could be found for this page.
        /// </summary>
        public string MissingMessage { get; set; } = "";

        /// <summary>
        /// Board camera only: false when the current site has no camera registered with the API,
        /// so the page can explain that instead of trying to connect.
        /// </summary>
        public bool IsConfigured { get; set; } = true;

        /// <summary>
        /// Board camera only: true when the page was opened from the "Live camera" option, so the
        /// camera is woken straight away. Otherwise the user has to switch it on.
        /// </summary>
        public bool StartCamera { get; set; }

        public bool SensorsOnline { get; set; }
        public string SensorSummary { get; set; } = "Waiting for sensors to report in.";
        public string? LatestPhotoUrl { get; set; }
        public DateTime? LatestPhotoTakenAt { get; set; }
        public long? LatestPhotoId { get; set; }
        public string TargetMac { get; set; } = "A8:42:E3:4C:54:08";
    }
}