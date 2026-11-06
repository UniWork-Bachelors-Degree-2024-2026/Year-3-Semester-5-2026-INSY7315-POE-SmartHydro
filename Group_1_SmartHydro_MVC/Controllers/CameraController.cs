using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class CameraController : Controller
    {
        private const string UploadedCamera = "Uploaded";
        private const string DeviceCameraName = "Device camera";
        private const string BoardSource = "board";
        private const string DefaultBoardCameraName = "ESP32-CAM";
        private const int MaxTextLength = 60;
        private const string ChooseStructureMessage = "Choose which structure this photo belongs to.";
        private const int BoardPhotoFreshMinutes = 5;
        private const string NoCameraMessage = "No camera is linked to this structure.";

        private readonly StructureApiClient _structureApi;
        private readonly CameraApiClient _cameraApi;
        private readonly ISiteContext _siteContext;
        private readonly ICameraDeviceService _cameraDevices;
        private readonly TelemetryApiClient _telemetry;

        private List<StructureViewModel>? _siteStructures;

        // Creates the controller and stores the injected services. The structure service supplies valid structure names, the camera API client stores and reads the photos and controls the board cameras, and the current site decides which photos are shown.
        public CameraController(StructureApiClient structureApi, CameraApiClient cameraApi, ISiteContext siteContext, ICameraDeviceService cameraDevices, TelemetryApiClient telemetry)
        {
            _structureApi = structureApi;
            _cameraApi = cameraApi;
            _siteContext = siteContext;
            _cameraDevices = cameraDevices;
            _telemetry = telemetry;
        }

        // Displays the photo gallery. Loads the photos the API holds for each structure in the current site, converts each one into a view model (including a URL to load its image), sorts them newest first, and supplies the list of structure names for the upload form.
        public async Task<IActionResult> Index()
        {
            var structures = await SiteStructuresAsync();

            var model = new GalleryViewModel
            {
                Today = DateTime.Now.Date,
                UploadStructures = structures.Select(s => s.Name).ToList()
            };

            foreach (var device in await _cameraDevices.GetForStructuresAsync(structures, User.GetAccessToken(), HttpContext.RequestAborted))
            {
                if (string.IsNullOrWhiteSpace(device.MacAddress))
                {
                    continue;
                }

                model.BoardCameras.Add(new BoardCameraOptionViewModel
                {
                    StructureId = device.StructureId,
                    StructureName = CameraStructureName(device, structures),
                    CameraName = CameraName(device)
                });
            }

            foreach (var structure in structures)
            {
                if (!int.TryParse(structure.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var structureId))
                {
                    continue;
                }

                foreach (var stored in await _cameraApi.GetGalleryAsync(structureId, User.GetAccessToken(), HttpContext.RequestAborted))
                {
                    model.Captures.Add(new CaptureViewModel
                    {
                        Id = stored.Id.ToString(CultureInfo.InvariantCulture),
                        Structure = structure.Name,
                        Plant = stored.Plant ?? "",
                        Camera = stored.CameraLabel ?? "",
                        Label = stored.Label ?? "",
                        TakenAt = ToUtc(stored.CapturedAt).ToLocalTime(),
                        IsFavourite = stored.IsFavourite,
                        ImageUrl = Url.Action(nameof(Photo), "Camera", new { id = stored.Id, v = PhotoVersion(stored) }) ?? ""
                    });
                }
            }

            model.Captures = model.Captures.OrderByDescending(c => c.TakenAt).ToList();

            return View(model);
        }

        // Displays the "Device camera" page, loading the latest high-res crop photo from the ESP32-CAM and live telemetry overlay
        public async Task<IActionResult> DeviceCamera(string? camera = null)
        {
            var token = User.GetAccessToken();
            var structures = await SiteStructuresAsync();
            var cameras = await _cameraDevices.GetForStructuresAsync(structures, token, HttpContext.RequestAborted);
            var device = cameras.FirstOrDefault(c => string.Equals(c.StructureId, camera, StringComparison.OrdinalIgnoreCase));
            if (device == null && string.IsNullOrWhiteSpace(camera) && cameras.Count > 0)
            {
                device = cameras[0];
            }

            // Only a camera linked to one of this site's structures can be used, so there is nothing to show without one
            if (device == null || string.IsNullOrWhiteSpace(device.MacAddress))
            {
                TempData["CameraError"] = "No ESP32-CAM is linked to a structure in this site. Add its MAC address when you edit the structure.";
                return RedirectToAction(nameof(Index));
            }

            var targetMac = device.MacAddress;
            var label = $"{CameraName(device)} - {CameraStructureName(device, structures)}";

            var (online, summary) = await LoadSensorSummaryAsync(structures, device.StructureId);

            int? structId = int.TryParse(device.StructureId, NumberStyles.None, CultureInfo.InvariantCulture, out var sid) ? sid : null;

            var latest = await _cameraApi.GetLatestAsync(targetMac, token, HttpContext.RequestAborted);
            if (latest == null && structId.HasValue)
            {
                latest = await _cameraApi.GetLatestForStructureAsync(structId.Value, token, HttpContext.RequestAborted);
            }

            string? latestPhotoUrl = null;
            DateTime? latestPhotoTakenAt = null;
            long? latestPhotoId = null;

            if (latest != null && latest.Id > 0)
            {
                latestPhotoId = latest.Id;
                latestPhotoTakenAt = ToUtc(latest.CapturedAt).ToLocalTime();
                latestPhotoUrl = Url.Action(nameof(Photo), "Camera", new { id = latest.Id, v = PhotoVersion(latest) });
            }

            return View(new CameraPageViewModel
            {
                CameraLabel = label.ToUpperInvariant(),
                CameraStructure = CameraStructureName(device, structures),
                CameraStructureId = device.StructureId,
                Structures = structures.Select(s => s.Name).ToList(),
                SensorsOnline = online,
                SensorSummary = summary,
                LatestPhotoId = latestPhotoId,
                LatestPhotoUrl = latestPhotoUrl,
                LatestPhotoTakenAt = latestPhotoTakenAt,
                TargetMac = targetMac
            });
        }

        // Triggers the ESP32-CAM to take an on-demand high-resolution photo and awaits the uploaded image
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerEspCapture(string? mac = null, int? structureId = null, bool flash = false)
        {
            var token = User.GetAccessToken();
            if (string.IsNullOrWhiteSpace(mac) && !structureId.HasValue)
            {
                return Json(new { success = false, message = NoCameraMessage });
            }

            var targetMac = mac ?? "";

            // Trigger the capture via SignalR and queue in API
            var (result, requestedAt) = await _cameraApi.TriggerCaptureAsync(targetMac, token, flash, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return Json(new { success = false, message = result.Message });
            }

            // Await photo upload from node (polling up to 25 seconds for camera capture and network upload)
            var deadline = DateTime.UtcNow.AddSeconds(25);
            ApiCapture? newest = null;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(750, HttpContext.RequestAborted);
                if (!string.IsNullOrWhiteSpace(targetMac))
                {
                    newest = await _cameraApi.GetLatestAsync(targetMac, token, HttpContext.RequestAborted);
                }
                if ((newest == null || ToUtc(newest.CapturedAt) < requestedAt.AddSeconds(-3)) && structureId.HasValue)
                {
                    var structNewest = await _cameraApi.GetLatestForStructureAsync(structureId.Value, token, HttpContext.RequestAborted);
                    if (structNewest != null && ToUtc(structNewest.CapturedAt) >= requestedAt.AddSeconds(-3))
                    {
                        newest = structNewest;
                    }
                }

                if (newest != null && newest.Id > 0 && ToUtc(newest.CapturedAt) >= requestedAt.AddSeconds(-3))
                {
                    break;
                }
            }

            if (newest != null && newest.Id > 0)
            {
                var structures = await SiteStructuresAsync();
                var (online, summary) = await LoadSensorSummaryAsync(structures, structureId?.ToString());

                return Json(new
                {
                    success = true,
                    photoId = newest.Id,
                    imageUrl = Url.Action(nameof(Photo), "Camera", new { id = newest.Id, v = PhotoVersion(newest) }),
                    takenAt = ToUtc(newest.CapturedAt).ToLocalTime().ToString("MMM d, yyyy - hh:mm:ss tt"),
                    sensorsOnline = online,
                    sensorSummary = summary
                });
            }

            return Json(new { success = false, message = "Capture timed out. Ensure ESP32-CAM is powered and connected to Wi-Fi." });
        }

        // Retrieves the latest photo metadata and live telemetry for display refresh
        [HttpGet]
        public async Task<IActionResult> DeviceCameraLatestPhoto(string? mac = null, int? structureId = null)
        {
            var token = User.GetAccessToken();
            if (string.IsNullOrWhiteSpace(mac) && !structureId.HasValue)
            {
                return Json(new { success = false, message = NoCameraMessage });
            }

            ApiCapture? latest = null;
            if (!string.IsNullOrWhiteSpace(mac))
            {
                latest = await _cameraApi.GetLatestAsync(mac, token, HttpContext.RequestAborted);
            }
            if (latest == null && structureId.HasValue)
            {
                latest = await _cameraApi.GetLatestForStructureAsync(structureId.Value, token, HttpContext.RequestAborted);
            }

            if (latest != null && latest.Id > 0)
            {
                var structures = await SiteStructuresAsync();
                var (online, summary) = await LoadSensorSummaryAsync(structures, structureId?.ToString());

                return Json(new
                {
                    success = true,
                    photoId = latest.Id,
                    imageUrl = Url.Action(nameof(Photo), "Camera", new { id = latest.Id, v = PhotoVersion(latest) }),
                    takenAt = ToUtc(latest.CapturedAt).ToLocalTime().ToString("MMM d, yyyy - hh:mm:ss tt"),
                    sensorsOnline = online,
                    sensorSummary = summary
                });
            }
            return Json(new { success = false, message = "No photos captured yet." });
        }

        // Retrieves photos captured on a specific date for a device (Date Picker)
        [HttpGet]
        public async Task<IActionResult> DeviceCameraPhotosByDate(string? mac, string date, int? structureId = null)
        {
            var token = User.GetAccessToken();
            if (string.IsNullOrWhiteSpace(mac) && !structureId.HasValue)
            {
                return Json(new { success = false, message = NoCameraMessage, photos = Array.Empty<object>() });
            }

            List<ApiCapture> photos = new();
            if (!string.IsNullOrWhiteSpace(mac))
            {
                photos = await _cameraApi.GetPhotosByDateAsync(mac, date, token, HttpContext.RequestAborted);
            }
            if (photos.Count == 0 && structureId.HasValue)
            {
                photos = await _cameraApi.GetPhotosByDateAndStructureAsync(structureId.Value, date, token, HttpContext.RequestAborted);
            }

            var items = photos.Select(p => new
            {
                id = p.Id,
                imageUrl = Url.Action(nameof(Photo), "Camera", new { id = p.Id, v = PhotoVersion(p) }),
                takenAt = ToUtc(p.CapturedAt).ToLocalTime().ToString("MMM d, yyyy - hh:mm:ss tt"),
                timeOnly = ToUtc(p.CapturedAt).ToLocalTime().ToString("hh:mm:ss tt")
            }).ToList();

            return Json(new { success = true, photos = items });
        }

        // Old address of the device camera page, kept so existing links and bookmarks still work.
        public IActionResult Live()
        {
            return RedirectToAction(nameof(DeviceCamera));
        }



        // Redirects legacy board camera links to the on-demand ESP32-CAM capture page
        public IActionResult BoardCamera(string? camera = null, bool start = false)
        {
            return RedirectToAction(nameof(DeviceCamera), new { camera });
        }

        // Handles a photo uploaded through the gallery form. Validates the file, cleans the structure, plant and label values, sends the photo to the API, and redirects back to the gallery with a success or error message.
        [HttpPost]
        /* Microsoft Learn (2026c) explains using [ValidateAntiForgeryToken] to protect POST actions from cross-site request forgery. */
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile? photo, string? structure, string? plant, string? takenAt, bool favourite = false)
        {
            var site = await _siteContext.GetCurrentAsync();
            if (site == null)
            {
                TempData["CameraError"] = "Choose a site before adding photos.";
                return RedirectToAction(nameof(Index));
            }

            var check = await ReadPhotoAsync(photo);

            if (check.Data == null)
            {
                TempData["CameraError"] = check.Error;
                return RedirectToAction(nameof(Index));
            }

            var target = await FindStructureAsync(structure);
            if (target == null || !int.TryParse(target.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var structureId))
            {
                TempData["CameraError"] = ChooseStructureMessage;
                return RedirectToAction(nameof(Index));
            }

            var label = CleanText(Path.GetFileNameWithoutExtension(photo?.FileName ?? ""));

            var saved = await _cameraApi.UploadAsync(new PhotoUpload
            {
                Data = check.Data,
                Extension = check.Extension,
                StructureId = structureId,
                Plant = CleanText(plant),
                Label = label,
                CameraLabel = UploadedCamera,
                CapturedAtUtc = ParseTakenAt(takenAt),
                IsFavourite = favourite
            }, User.GetAccessToken(), HttpContext.RequestAborted);

            if (!saved.Success)
            {
                TempData["CameraError"] = saved.Message;
                return RedirectToAction(nameof(Index));
            }

            if (favourite)
            {
                TempData["CameraMessage"] = "Photo added to the gallery and your favorites.";
            }
            else
            {
                TempData["CameraMessage"] = "Photo added to the gallery.";
            }
            return RedirectToAction(nameof(Index));
        }

        // Saves a photo taken on the device camera or board camera page. Validates the image and records which camera took it. A device camera photo is sent to the API as a new photo. A board camera photo is already stored by the API, so its details are filled in instead. Returns a JSON result so the page can show feedback without reloading.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Capture(IFormFile? photo, string? source = null, string? camera = null, string? structure = null, string? plant = null, string? takenAt = null, bool favourite = false)
        {
            var site = await _siteContext.GetCurrentAsync();
            if (site == null)
            {
                return BadRequest(new { success = false, message = "Choose a site before saving photos." });
            }

            var check = await ReadPhotoAsync(photo);

            if (check.Data == null)
            {
                return BadRequest(new { success = false, message = check.Error });
            }

            var target = await FindStructureAsync(structure);
            if (target == null || !int.TryParse(target.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var structureId))
            {
                return BadRequest(new { success = false, message = ChooseStructureMessage });
            }

            var token = User.GetAccessToken();
            var aborted = HttpContext.RequestAborted;
            var takenUtc = ParseTakenAt(takenAt);
            var cameraName = DeviceCameraName;
            string? deviceMac = null;
            ApiCapture? boardPhoto = null;

            if (string.Equals(source, BoardSource, StringComparison.OrdinalIgnoreCase))
            {
                var device = await FindCameraAsync(camera);
                cameraName = CameraName(device);

                if (device != null && !string.IsNullOrWhiteSpace(device.MacAddress))
                {
                    deviceMac = device.MacAddress;
                    var latest = await _cameraApi.GetLatestAsync(device.MacAddress, token, aborted);
                    if (latest != null && DateTime.UtcNow - ToUtc(latest.CapturedAt) < TimeSpan.FromMinutes(BoardPhotoFreshMinutes))
                    {
                        boardPhoto = latest;
                    }
                }
            }

            CameraApiResult result;
            if (boardPhoto != null)
            {
                result = await _cameraApi.UpdateAsync(boardPhoto.Id, new PhotoChanges
                {
                    StructureId = structureId,
                    Plant = CleanText(plant),
                    Label = cameraName + " photo",
                    CameraLabel = cameraName,
                    CapturedAtUtc = takenUtc,
                    IsFavourite = favourite
                }, token, aborted);
            }
            else
            {
                result = await _cameraApi.UploadAsync(new PhotoUpload
                {
                    Data = check.Data,
                    Extension = check.Extension,
                    StructureId = structureId,
                    Plant = CleanText(plant),
                    Label = cameraName + " photo",
                    CameraLabel = cameraName,
                    DeviceMac = deviceMac,
                    CapturedAtUtc = takenUtc,
                    IsFavourite = favourite
                }, token, aborted);
            }

            if (!result.Success)
            {
                return BadRequest(new { success = false, message = result.Message });
            }

            return Json(new { success = true, message = "Photo saved to the gallery." });
        }

        // Edits an existing photo. Updates the structure, plant, date taken and favourite state, and optionally replaces the image with a new upload or a photo taken with the camera. Returns JSON so the pop-up can show errors without closing; on success a message is queued for the gallery, which reloads to show the changes.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string? id, IFormFile? photo, string? source, string? structure, string? plant, string? takenAt, bool favourite = false)
        {
            var existing = await FindInCurrentSiteAsync(id);

            if (existing == null)
            {
                return NotFound(new { success = false, message = "That photo could not be found. It may have been deleted." });
            }

            byte[]? data = null;
            var extension = "";
            string? camera = null;

            if (photo != null && photo.Length > 0)
            {
                var check = await ReadPhotoAsync(photo);

                if (check.Data == null)
                {
                    return BadRequest(new { success = false, message = check.Error });
                }

                data = check.Data;
                extension = check.Extension;
                if (string.Equals(source, "camera", StringComparison.Ordinal))
                {
                    camera = DeviceCameraName;
                }
                else
                {
                    camera = UploadedCamera;
                }
            }

            var wantedStructure = (structure ?? "").Trim();
            StructureViewModel? target;
            if (string.Equals(wantedStructure, existing.Structure.Name, StringComparison.Ordinal))
            {
                target = existing.Structure;
            }
            else
            {
                target = await FindStructureAsync(wantedStructure);
            }

            if (target == null || !int.TryParse(target.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var structureId))
            {
                return BadRequest(new { success = false, message = ChooseStructureMessage });
            }

            var takenUtc = ParseTakenAt(takenAt);

            if (takenUtc.HasValue && takenUtc.Value.ToLocalTime().Date == ToUtc(existing.Capture.CapturedAt).ToLocalTime().Date)
            {
                // Same day as before, so keep the original time of day.
                takenUtc = null;
            }

            var changes = new PhotoChanges
            {
                Data = data,
                Extension = extension,
                StructureId = structureId,
                Plant = CleanText(plant),
                CameraLabel = camera,
                CapturedAtUtc = takenUtc,
                IsFavourite = favourite
            };

            var result = await _cameraApi.UpdateAsync(existing.Capture.Id, changes, User.GetAccessToken(), HttpContext.RequestAborted);

            if (!result.Success)
            {
                if (result.Status == HttpStatusCode.NotFound)
                {
                    return NotFound(new { success = false, message = "That photo could not be found. It may have been deleted." });
                }

                return BadRequest(new { success = false, message = result.Message });
            }

            TempData["CameraMessage"] = "Photo updated.";
            return Json(new { success = true });
        }

        // Deletes a photo through the API and redirects back to the gallery with a confirmation message, or an error message if the photo could not be found or removed.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string? id)
        {
            var capture = await FindInCurrentSiteAsync(id);
            if (capture == null || !await _cameraApi.DeleteAsync(capture.Capture.Id, User.GetAccessToken(), HttpContext.RequestAborted))
            {
                TempData["CameraError"] = "That photo could not be deleted. It may already have been deleted, or you may not be allowed to remove it.";
                return RedirectToAction(nameof(Index));
            }

            TempData["CameraMessage"] = "Photo moved to deleted items.";
            TempData["CameraUndoId"] = capture.Capture.Id.ToString(CultureInfo.InvariantCulture);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(string? id)
        {
            if (!TryParseId(id, out var photoId)
                || !await _cameraApi.RestoreAsync(photoId, User.GetAccessToken(), HttpContext.RequestAborted))
            {
                TempData["CameraError"] = "That photo could not be restored.";
                return RedirectToAction(nameof(Index));
            }

            TempData["CameraMessage"] = "Photo restored.";
            return RedirectToAction(nameof(Index));
        }

        // Marks or unmarks a photo as a favourite. Returns JSON so the gallery can update the favourite state without reloading the page.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Favourite(string? id, bool favourite)
        {
            var capture = await FindInCurrentSiteAsync(id);
            if (capture == null || !await _cameraApi.SetFavouriteAsync(capture.Capture.Id, favourite, User.GetAccessToken(), HttpContext.RequestAborted))
            {
                return NotFound(new { success = false, message = "That photo could not be found. It may have been deleted." });
            }

            return Json(new { success = true, favourite });
        }

        // Serves a stored photo so it can be displayed in the browser. The API only returns photos the signed-in user may see. Adds security and caching headers before returning the image.
        [HttpGet]
        public async Task<IActionResult> Photo(string id)
        {
            if (!TryParseId(id, out var photoId))
            {
                return NotFound();
            }

            var photo = await _cameraApi.GetPhotoAsync(photoId, User.GetAccessToken(), HttpContext.RequestAborted);

            if (photo == null)
            {
                return NotFound();
            }

            /* Microsoft Learn (2026b) demonstrates setting HTTP response headers using Response.Headers. */
            /* OWASP (2026) recommends X-Content-Type-Options: nosniff to prevent MIME type sniffing. */
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            /* Mozilla Developer (2026a) documents Cache-Control directives such as private and max-age. */
            Response.Headers["Cache-Control"] = "private, max-age=86400";

            return File(photo.Value.Data, photo.Value.ContentType);
        }

        // Sends a stored photo as a file download rather than displaying it, using a friendly generated file name.
        [HttpGet]
        public async Task<IActionResult> Download(string id)
        {
            var capture = await FindInCurrentSiteAsync(id);

            if (capture == null)
            {
                return NotFound();
            }

            var photo = await _cameraApi.GetPhotoAsync(capture.Capture.Id, User.GetAccessToken(), HttpContext.RequestAborted);

            if (photo == null)
            {
                return NotFound();
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";

            /* Microsoft Learn (2026a) shows that passing a fileDownloadName to File() makes the response a download. */
            /* Mozilla Developer (2026b) documents Content-Disposition: attachment, which tells the browser to save the file instead of showing it. */
            return File(photo.Value.Data, photo.Value.ContentType, DownloadName(capture.Capture, capture.Structure.Name, photo.Value.ContentType));
        }

        // Finds the camera fitted to one of the current site's structures
        private async Task<CameraDeviceViewModel?> FindCameraAsync(string? structureId)
        {
            var structures = await SiteStructuresAsync();
            var structure = structures.FirstOrDefault(x => string.Equals(x.Id, structureId, StringComparison.OrdinalIgnoreCase));

            return await _cameraDevices.FindForStructureAsync(structure, User.GetAccessToken(), HttpContext.RequestAborted);
        }

        // Loads latest real-time telemetry readings to display directly on the live camera overlay
        private async Task<(bool Online, string Summary)> LoadSensorSummaryAsync(List<StructureViewModel> structures, string? preferredStructureId = null)
        {
            var target = structures.FirstOrDefault(s => string.Equals(s.Id, preferredStructureId, StringComparison.OrdinalIgnoreCase))
                         ?? structures.FirstOrDefault();

            if (target == null || string.IsNullOrWhiteSpace(target.MacAddress))
            {
                return (false, "No structure sensor bound.");
            }

            try
            {
                var token = User.GetAccessToken();
                var readings = await _telemetry.GetDashboardReadingsAsync(target, token, HttpContext.RequestAborted);
                if (readings.HasTelemetry)
                {
                    var parts = new List<string>();
                    if (readings.TemperatureC > 0) parts.Add($"{readings.TemperatureC:F1}°C");
                    if (readings.HumidityPercent > 0) parts.Add($"{readings.HumidityPercent:F0}% RH");
                    if (readings.Ph > 0) parts.Add($"pH {readings.Ph:F1}");
                    if (readings.EcMs > 0) parts.Add($"EC {readings.EcMs:F1} mS");
                    if (readings.WaterLevelDetected) parts.Add("Water OK");

                    var text = parts.Count > 0 ? string.Join(" | ", parts) : "Sensors reporting nominal.";
                    return (true, text);
                }
            }
            catch { }

            return (false, "Sensors awaiting telemetry.");
        }

        // Loads the current site's structures from the API once per request, or none when no site is chosen
        private async Task<List<StructureViewModel>> SiteStructuresAsync()
        {
            if (_siteStructures != null)
            {
                return _siteStructures;
            }

            _siteStructures = new List<StructureViewModel>();

            var siteId = await CurrentSiteIdAsync();
            if (siteId != null)
            {
                var loaded = await _structureApi.GetStructuresAsync(siteId, User.GetAccessToken(), HttpContext.RequestAborted);
                if (loaded != null)
                {
                    _siteStructures = loaded;
                }
            }

            return _siteStructures;
        }

        // Returns the id of the current site after checking the user can still see it, or null when none is chosen
        private async Task<string?> CurrentSiteIdAsync()
        {
            var site = await _siteContext.GetCurrentAsync();
            if (site == null)
            {
                return null;
            }

            return site.Id;
        }

        // Gets the name to show and save for a camera, falling back to "ESP32-CAM".
        private static string CameraName(CameraDeviceViewModel? device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.DeviceName))
            {
                return DefaultBoardCameraName;
            }

            return device.DeviceName.Trim();
        }

        // Gets the name of the structure a camera is fitted to, so photos from it are tagged with that structure straight away
        private static string CameraStructureName(CameraDeviceViewModel? device, List<StructureViewModel> structures)
        {
            if (device == null)
            {
                return "";
            }

            var structure = structures.FirstOrDefault(x => string.Equals(x.Id, device.StructureId, StringComparison.OrdinalIgnoreCase));
            if (structure == null)
            {
                return "";
            }

            return structure.Name;
        }

        // Gets the names of the current site's structures for the Structure drop-down when saving a photo
        private async Task<List<string>> StructureNamesAsync()
        {
            var structures = await SiteStructuresAsync();
            return structures.Select(s => s.Name).ToList();
        }

        // Reads a photo id from the address, returning false for anything that is not a whole number
        private static bool TryParseId(string? id, out long photoId)
        {
            return long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out photoId);
        }

        // Treats a date from the API as UTC when it carries no time zone
        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        // Finds a photo by id only when it sits on one of the current site's structures, so photos from another site cannot be edited or deleted
        private async Task<SitePhoto?> FindInCurrentSiteAsync(string? id)
        {
            if (!TryParseId(id, out var photoId))
            {
                return null;
            }

            var capture = await _cameraApi.GetMetadataAsync(photoId, User.GetAccessToken(), HttpContext.RequestAborted);
            if (capture == null || !capture.StructureId.HasValue)
            {
                return null;
            }

            var wantedId = capture.StructureId.Value.ToString(CultureInfo.InvariantCulture);
            var structures = await SiteStructuresAsync();
            var structure = structures.FirstOrDefault(s => string.Equals(s.Id, wantedId, StringComparison.Ordinal));
            if (structure == null)
            {
                return null;
            }

            return new SitePhoto(capture, structure);
        }

        // Returns a value that changes whenever a photo's image is replaced or its details change. It is added to the image URL so the browser does not keep showing an old cached copy after an edit.
        private static string PhotoVersion(ApiCapture capture)
        {
            return capture.ImageSizeBytes.ToString(CultureInfo.InvariantCulture) + "-" + ToUtc(capture.CapturedAt).Ticks.ToString(CultureInfo.InvariantCulture);
        }

        // One photo together with the structure it sits on, both found in the current site
        private sealed record SitePhoto(ApiCapture Capture, StructureViewModel Structure);

        // Builds a safe, readable file name for a downloaded photo. Uses the photo's label (or its structure and plant if there is no label), keeps only lowercase letters and digits separated by single dashes, then appends the date taken and the file extension. Falls back to "smarthydro-photo" if nothing usable remains.
        private static string DownloadName(ApiCapture capture, string structureName, string? contentType)
        {
            var source = capture.Label ?? "";

            if (source.Trim().Length == 0)
            {
                source = (structureName + " " + capture.Plant).Trim();
            }

            var builder = new System.Text.StringBuilder();
            var lastWasDash = false;

            foreach (var ch in source.Trim().ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    builder.Append(ch);
                    lastWasDash = false;
                }
                else if (!lastWasDash && builder.Length > 0)
                {
                    builder.Append('-');
                    lastWasDash = true;
                }
            }

            var name = builder.ToString().Trim('-');

            if (name.Length == 0)
            {
                name = "smarthydro-photo";
            }

            var takenOn = ToUtc(capture.CapturedAt).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return name + "-" + takenOn + CameraPhotoFile.ExtensionFor(contentType);
        }

        // Reads and validates an uploaded photo. Checks that a file was provided, that it is within the size limit, and that its contents are a supported image type (JPEG, PNG or WebP). Returns the image bytes and extension, or an error message if invalid.
        private static async Task<(byte[]? Data, string Extension, string Error)> ReadPhotoAsync(IFormFile? photo)
        {
            if (photo == null || photo.Length == 0)
            {
                return (null, "", "Choose a photo first.");
            }

            if (photo.Length > CameraPhotoFile.MaxBytes)
            {
                return (null, "", "That photo is too large. The limit is 10 MB.");
            }

            using var buffer = new MemoryStream();
            await photo.CopyToAsync(buffer);
            var data = buffer.ToArray();

            if (!CameraPhotoFile.TryDetectImage(data, out var extension))
            {
                return (null, "", "That file isn't a supported photo. Use a JPEG, PNG or WebP image.");
            }

            return (data, extension, "");
        }

        // Finds the current site's structure whose name matches exactly. Returns null when no name was given or nothing matches, so photos are only ever saved on a real structure in this site.
        private async Task<StructureViewModel?> FindStructureAsync(string? structure)
        {
            var wanted = (structure ?? "").Trim();

            if (wanted.Length == 0)
            {
                return null;
            }

            var structures = await SiteStructuresAsync();
            return structures.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.Ordinal));
        }

        // Converts the submitted "taken on" date (yyyy-MM-dd) into a UTC date and time. The current time of day is added to the date, and future times are capped at now. Returns null if the value is empty or not a valid date.
        private static DateTime? ParseTakenAt(string? takenAt)
        {
            var value = (takenAt ?? "").Trim();

            if (value.Length == 0)
            {
                return null;
            }

            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return null;
            }

            var now = DateTime.Now;
            var local = day.Date + now.TimeOfDay;

            if (local > now)
            {
                local = now;
            }

            return local.ToUniversalTime();
        }

        // Cleans user-entered text by removing control characters, trimming whitespace, and shortening it to the maximum allowed length.
        private static string CleanText(string? value)
        {
            var cleaned = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();

            if (cleaned.Length > MaxTextLength)
            {
                cleaned = cleaned.Substring(0, MaxTextLength).TrimEnd();
            }

            return cleaned;
        }
    }
}

//Reference Lists:

//Microsoft Learn, 2026a. ControllerBase.PhysicalFile Method. [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.controllerbase.physicalfile> [Accessed 22 September 2026].

//Microsoft Learn, 2026b. HttpResponse.Headers Property. [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpresponse.headers> [Accessed 22 September 2026].

//Microsoft Learn, 2026c. Prevent Cross-Site Request Forgery (XSRF/CSRF) attacks in ASP.NET Core. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery> [Accessed 22 September 2026].

//Mozilla Developer, 2026a. Cache-Control header. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cache-Control> [Accessed 22 September 2026].

//Mozilla Developer, 2026b. Content-Disposition header. [online] Available at: <https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Disposition> [Accessed 22 September 2026].

//OWASP, 2026. HTTP Headers Cheat Sheet. [online] Available at: <https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html> [Accessed 22 September 2026].