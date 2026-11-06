using System.Globalization;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    public interface ICameraDeviceService
    {
        Task<IReadOnlyList<CameraDeviceViewModel>> GetForStructuresAsync(IEnumerable<StructureViewModel> structures, string accessToken, CancellationToken ct = default);

        Task<CameraDeviceViewModel?> FindForStructureAsync(StructureViewModel? structure, string accessToken, CancellationToken ct = default);
    }

    public class CameraDeviceService : ICameraDeviceService
    {
        private const string CameraNodeType = "CAMERA-NODE";

        private readonly SmartHydroApiHttpClient _api;

        public CameraDeviceService(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Collects the camera fitted to each of the given structures, in the same order, skipping structures without one
        public async Task<IReadOnlyList<CameraDeviceViewModel>> GetForStructuresAsync(IEnumerable<StructureViewModel> structures, string accessToken, CancellationToken ct = default)
        {
            var cameras = new List<CameraDeviceViewModel>();

            foreach (var structure in structures)
            {
                var camera = await FindForStructureAsync(structure, accessToken, ct);
                if (camera != null)
                {
                    cameras.Add(camera);
                }
            }

            return cameras;
        }

        // Reads the camera fitted to a structure from the API's dashboard summary, which only returns structures the user may see
        public async Task<CameraDeviceViewModel?> FindForStructureAsync(StructureViewModel? structure, string accessToken, CancellationToken ct = default)
        {
            if (structure == null || string.IsNullOrWhiteSpace(structure.CameraMac))
            {
                return null;
            }

            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Structures/{Uri.EscapeDataString(structure.Id)}/dashboard-summary", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var summary = await SmartHydroApiHttpClient.ReadJsonAsync<DashboardSummaryDto?>(response, null, ct);

            if (summary == null || summary.CameraDevice == null)
            {
                return null;
            }

            var device = summary.CameraDevice;

            return new CameraDeviceViewModel
            {
                MacAddress = device.MacAddress,
                DeviceName = device.DeviceName,
                DeviceType = CameraNodeType,
                StructureId = summary.StructureId.ToString(CultureInfo.InvariantCulture),
                LocalIp = device.LocalIp ?? "",
                LastSeenAt = device.LastSeenAt
            };
        }

        private sealed class DashboardSummaryDto
        {
            public int StructureId { get; set; }
            public DeviceDto? CameraDevice { get; set; }
        }

        private sealed class DeviceDto
        {
            public string MacAddress { get; set; } = "";
            public string DeviceName { get; set; } = "";
            public string? LocalIp { get; set; }
            public DateTime LastSeenAt { get; set; }
        }
    }
}