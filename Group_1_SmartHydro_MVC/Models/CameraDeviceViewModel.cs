namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// A camera board as the SmartHydro API describes it. It mirrors the API's Device entity for
    /// a "CAMERA-NODE": the board's MAC address, its name, the structure it is fitted to, the
    /// local IP address it reported when it last connected, and when it was last seen.
    /// </summary>
    public class CameraDeviceViewModel
    {
        public string MacAddress { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "CAMERA-NODE";
        public string StructureId { get; set; } = "";
        public string LocalIp { get; set; } = "";
        public DateTime LastSeenAt { get; set; }
    }

    /// <summary>
    /// One structure's camera as it is offered in the "Live camera" part of "Add a photo".
    /// </summary>
    public class BoardCameraOptionViewModel
    {
        public string StructureId { get; set; } = "";
        public string StructureName { get; set; } = "";
        public string CameraName { get; set; } = "";
    }
}