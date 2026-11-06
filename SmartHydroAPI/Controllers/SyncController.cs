using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    // Controller managing Data-Mule batch synchronisation from Android RoomDB
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : ControllerBase
    {
        private readonly ISyncService _syncService;

        // Injects synchronisation service dependency
        public SyncController(ISyncService syncService)
        {
            _syncService = syncService;
        }

        // Returns newest stored timestamp for a device to prevent redundant mobile re-uploads
        [HttpGet("devices/{mac}/latest-timestamp")]
        [ProducesResponseType(typeof(HighWaterMarkResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<HighWaterMarkResponseDto>> GetLatestTimestamp(string mac, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid MAC Address",
                    Detail = "Device MAC address parameter cannot be empty.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var result = await _syncService.GetLatestTimestampAsync(mac, ct);
            return Ok(result);
        }

        // Ingests batch of cached sensor readings uploaded by mobile data-mule with deduplication
        [HttpPost("telemetry-batch")]
        [ProducesResponseType(typeof(TelemetryBatchSyncResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<TelemetryBatchSyncResponseDto>> IngestTelemetryBatch(
            [FromBody] TelemetryBatchSyncRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) 
                return BadRequest(ModelState);

            var result = await _syncService.SyncTelemetryBatchAsync(dto, ct);
            return Ok(result);
        }

        // Ingests batch of offline alerts raised by the hardware node
        [HttpPost("alerts-batch")]
        [ProducesResponseType(typeof(AlertsBatchSyncResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<AlertsBatchSyncResponseDto>> IngestAlertsBatch(
            [FromBody] AlertsBatchSyncRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) 
                return BadRequest(ModelState);

            var result = await _syncService.SyncAlertsBatchAsync(dto, ct);
            return Ok(result);
        }

        // Ingests batch of manual actuator overrides executed on-site while offline
        [HttpPost("overrides-batch")]
        [ProducesResponseType(typeof(OverridesBatchSyncResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<OverridesBatchSyncResponseDto>> IngestOverridesBatch(
            [FromBody] OverridesBatchSyncRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) 
                return BadRequest(ModelState);

            var result = await _syncService.SyncOverridesBatchAsync(dto, ct);
            return Ok(result);
        }
    }
}
