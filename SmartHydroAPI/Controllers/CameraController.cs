using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;
using Microsoft.AspNetCore.Authorization;

namespace SmartHydroAPI.Controllers
{
    // Controller managing camera captures, polling, gallery pagination, and image housekeeping
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CameraController : ControllerBase
    {
        private readonly CameraService _service;
        private readonly IHubContext<ActuatorHub> _hubContext;

        public CameraController(CameraService service, IHubContext<ActuatorHub> hubContext)
        {
            _service = service;
            _hubContext = hubContext;
        }

        // Polls the camera for a pending command (hardware endpoint - AllowAnonymous)
        [AllowAnonymous]
        [HttpGet("poll-command/{mac}")]
        public async Task<ActionResult<CameraPendingResponseDto>> PollCommand(string mac) =>
            Ok(await _service.PollPendingCommandAsync(mac));

        // Uploads image frame from microcontroller node or manual upload
        [AllowAnonymous]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadPhoto(
            [FromForm] IFormFile file,
            [FromForm] string? deviceMac = null,
            [FromForm] int? structureId = null,
            [FromForm] string? plant = null,
            [FromForm] string? label = null,
            [FromForm] string? cameraLabel = null,
            [FromForm] DateTimeOffset? capturedAt = null,
            [FromForm] bool isFavourite = false)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid File",
                    Detail = "No image file provided in upload request.",
                    Status = StatusCodes.Status400BadRequest
                });

            try
            {
                var captureId = await _service.UploadPhotoAsync(file, deviceMac, structureId, plant, label, cameraLabel, capturedAt, isFavourite, User);

                if (!string.IsNullOrWhiteSpace(deviceMac))
                {
                    var normalizedMac = deviceMac.Trim().ToUpperInvariant();
                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(normalizedMac).Replace(":", "")).SendAsync("PhotoCaptured", new
                    {
                        captureId,
                        deviceMac = normalizedMac,
                        imageUrl = $"/api/Camera/photos/{captureId}",
                        capturedAt = capturedAt ?? DateTimeOffset.UtcNow
                    });
                }

                return Ok(new { success = true, captureId, size = file.Length });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // Triggers an on-demand capture command via SignalR and pending DB queue
        [AllowAnonymous]
        [HttpPost("trigger-capture")]
        [HttpPost("trigger-capture/{mac}")]
        public async Task<IActionResult> TriggerCapture([FromBody] CameraTriggerRequestDto? dto = null, [FromRoute] string? mac = null, [FromQuery] bool? flash = null)
        {
            var targetMac = !string.IsNullOrWhiteSpace(mac) ? mac : dto?.DeviceMac;
            var withFlash = flash ?? dto?.Flash ?? false;
            if (string.IsNullOrWhiteSpace(targetMac))
                return BadRequest(new ProblemDetails
                {
                    Title = "Missing Device MAC",
                    Detail = "Device MAC address is required to trigger photo capture.",
                    Status = StatusCodes.Status400BadRequest
                });

            var command = await _service.TriggerCaptureAsync(targetMac, withFlash);

            var normalizedMac = command.DeviceMac;
            var noColons = normalizedMac.Replace(":", "");
            var structureId = await _service.GetStructureIdForDeviceAsync(targetMac);

            var triggerPayload = new
            {
                deviceMac = normalizedMac,
                structureId = structureId,
                requestedAt = command.RequestedAt,
                flash = withFlash
            };

            // Dispatch instant sub-50ms SignalR command to target node
            await _hubContext.Clients.Group(normalizedMac).SendAsync("TriggerCapture", triggerPayload);
            await _hubContext.Clients.Group(noColons).SendAsync("TriggerCapture", triggerPayload);


            return Ok(new { success = true, message = "Capture command queued.", requestedAt = command.RequestedAt, flash = withFlash, structureId });
        }

        // Returns the latest image binary for a device (AllowAnonymous proxy)
        [AllowAnonymous]
        [HttpGet("latest-image/{mac}")]
        public async Task<IActionResult> GetLatestImage(string mac, CancellationToken ct)
        {
            var latest = await _service.GetLatestImageAsync(mac, User, ct);
            return latest is null ? NotFound(new ProblemDetails
            {
                Title = "Image Not Found",
                Detail = $"No image captured for device '{mac}'.",
                Status = StatusCodes.Status404NotFound
            }) : File(latest.Value.Data, latest.Value.ContentType);
        }

        // Retrieves photo binary by ID
        [HttpGet("photos/{id:long}")]
        public async Task<IActionResult> GetPhotoById(long id, CancellationToken ct)
        {
            var photo = await _service.GetPhotoByIdAsync(id, User, ct);
            return photo is null ? NotFound(new ProblemDetails
            {
                Title = "Photo Not Found",
                Detail = $"Camera capture #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            }) : File(photo.Value.Data, photo.Value.ContentType);
        }

        // Retrieves photo metadata JSON by ID
        [HttpGet("photos/{id:long}/metadata")]
        public async Task<IActionResult> GetPhotoMetadata(long id, CancellationToken ct)
        {
            var metadata = await _service.GetPhotoMetadataByIdAsync(id, User, ct);
            return metadata is null ? NotFound(new ProblemDetails
            {
                Title = "Photo Not Found",
                Detail = $"Camera capture #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            }) : Ok(metadata);
        }

        // Soft-deletes a single camera photo
        [HttpDelete("photos/{id:long}")]
        public async Task<IActionResult> DeletePhoto(long id, CancellationToken ct)
        {
            var deleted = await _service.DeletePhotoAsync(id, User, ct);
            return deleted ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Photo Not Found",
                Detail = $"Camera capture #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        [HttpPost("photos/{id:long}/restore")]
        public async Task<IActionResult> RestorePhoto(long id, CancellationToken ct)
        {
            var restored = await _service.RestorePhotoAsync(id, User, ct);
            return restored ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Photo Not Found",
                Detail = $"Camera capture #{id} was not found in the deleted items you can restore.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Edits a photo's details (structure, plant, label, date taken, favourite) and optionally replaces its image
        [HttpPut("photos/{id:long}")]
        public async Task<IActionResult> UpdatePhoto(
            long id,
            [FromForm] IFormFile? file,
            [FromForm] int? structureId,
            [FromForm] string? plant,
            [FromForm] string? label,
            [FromForm] string? cameraLabel,
            [FromForm] DateTimeOffset? capturedAt,
            [FromForm] bool? isFavourite,
            CancellationToken ct)
        {
            // An empty text field arrives as null, so a field that was sent but left empty is passed on as empty text to clear the value
            if (plant is null && SentEmptyField(nameof(plant))) plant = string.Empty;
            if (label is null && SentEmptyField(nameof(label))) label = string.Empty;

            try
            {
                var updated = await _service.UpdatePhotoAsync(id, file, structureId, plant, label, cameraLabel, capturedAt, isFavourite, User, ct);
                return updated ? NoContent() : NotFound(new ProblemDetails
                {
                    Title = "Photo Not Found",
                    Detail = $"Camera capture #{id} was not found.",
                    Status = StatusCodes.Status404NotFound
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("photos/{id:long}/purge")]
        public async Task<IActionResult> PurgePhoto(long id, CancellationToken ct)
        {
            var purged = await _service.PurgePhotoAsync(id, ct);
            return purged ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Deleted Photo Not Found",
                Detail = $"Camera capture #{id} is not available for permanent purge.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Paginated gallery query with structure and device filtering
        [HttpGet("gallery")]
        public async Task<ActionResult<PagedResultDto<object>>> GetGallery(
            [FromQuery] int? structureId,
            [FromQuery] string? deviceMac,
            [FromQuery] string? date = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            CancellationToken ct = default)
        {
            return Ok(await _service.GetGalleryAsync(structureId, deviceMac, date, User, page, pageSize, ct));
        }

        // Gets a list of images for a structure (legacy route)
        [HttpGet("structure/{structureId:int}")]
        public async Task<IActionResult> GetStructureGallery(int structureId) =>
            Ok(await _service.GetStructureGalleryAsync(structureId, User));

        // Toggles the favourite status of an image, or sets it to the given value when one is supplied
        [HttpPut("{id:long}/favourite")]
        public async Task<IActionResult> ToggleFavourite(long id, [FromQuery] bool? value = null)
        {
            var isFavourite = await _service.ToggleFavouriteAsync(id, User, value);
            return isFavourite is null ? NotFound(new ProblemDetails
            {
                Title = "Photo Not Found",
                Detail = $"Camera capture #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            }) : Ok(new { id, isFavourite = isFavourite.Value });
        }

        // Checks whether the form carried the named field at all, even with nothing in it
        private bool SentEmptyField(string name) =>
            Request.HasFormContentType && Request.Form.ContainsKey(name);
    }
}
