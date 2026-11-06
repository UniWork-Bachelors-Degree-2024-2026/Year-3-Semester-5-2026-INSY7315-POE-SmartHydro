using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class ControlsController : Controller
    {
        private readonly StructureApiClient _structures;
        private readonly TelemetryApiClient _telemetry;

        // Creates the controller and stores the services used to find a structure's controller board and send it commands.
        public ControlsController(StructureApiClient structures, TelemetryApiClient telemetry)
        {
            _structures = structures;
            _telemetry = telemetry;
        }

        // Switches one control (pump, lights, fans or spray) on or off for a structure and replies with the state the API ended up with. Operators, workers and admins; changing the controller connection mode stays with operators and admins.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Set(string? structureId, string? control, bool on, CancellationToken ct)
        {
            if (!User.CanControlEquipment())
            {
                return Refuse(StatusCodes.Status403Forbidden, "You do not have permission to change controls.");
            }

            if (!ActuatorControls.IsKnown(control))
            {
                return Refuse(StatusCodes.Status400BadRequest, "That control is not recognised.");
            }

            if (on && ActuatorControls.NeedsDirectionToTurnOn(control!))
            {
                return Refuse(StatusCodes.Status400BadRequest, "Choose a dose direction (+ or -) to run a dosing pump.");
            }

            var accessToken = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, accessToken, ct);

            if (structure == null)
            {
                return Refuse(StatusCodes.Status404NotFound, "That structure could not be found.");
            }

            if (string.IsNullOrWhiteSpace(structure.MacAddress))
            {
                return Refuse(StatusCodes.Status400BadRequest, "This structure has no controller board linked yet.");
            }

            var outcome = await _telemetry.SendCommandAsync(structure.MacAddress, control!, on, accessToken, ct);

            if (!outcome.Ok)
            {
                return Refuse(outcome.StatusCode, outcome.Message);
            }

            return Json(new { ok = true, on = outcome.On, commandId = outcome.CommandId, requestId = outcome.RequestId, pending = outcome.Pending, message = "" });
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> CommandStatus(string? structureId, int commandId, CancellationToken ct, Guid? requestId = null)
        {
            var token = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, token, ct);
            if (structure == null || string.IsNullOrWhiteSpace(structure.MacAddress)) return NotFound();
            var status = await _telemetry.ReadCommandStatusAsync(structure.MacAddress, commandId, token, ct, requestId);
            return status.HasValue ? Json(status.Value) : StatusCode(503, new { message = "Command status is unavailable." });
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ModeState(string? structureId, CancellationToken ct)
        {
            var structure = await _structures.GetStructureAsync(structureId, User.GetAccessToken(), ct);
            if (structure == null || string.IsNullOrWhiteSpace(structure.MacAddress)) return NotFound();
            var state = await _telemetry.GetModeStateAsync(structure.MacAddress, User.GetAccessToken(), ct);
            if (!state.HasValue) return StatusCode(503, new { message = "Mode status is unavailable. Retrying..." });
            var payload = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(state.Value.GetRawText())!;
            payload["relayState"] = await _telemetry.ReadRelayStateAsync(structure.MacAddress, User.GetAccessToken(), ct);
            return Json(payload);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> TriggerLoraMode(string? structureId, CancellationToken ct) => RequestMode(structureId, 0, ct);
        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> TriggerOnlineMode(string? structureId, CancellationToken ct) => RequestMode(structureId, 1, ct);
        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> TriggerOfflineMode(string? structureId, CancellationToken ct) => RequestMode(structureId, 2, ct);

        private async Task<IActionResult> RequestMode(string? structureId, int mode, CancellationToken ct)
        {
            if (!User.CanManageSites()) return Forbid();
            var token = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, token, ct);
            if (structure == null || string.IsNullOrWhiteSpace(structure.MacAddress))
                return Refuse(404, "This structure has no controller linked.");
            var ok = mode switch {
                0 => await _telemetry.RequestLoraModeAsync(structure.MacAddress, token, ct),
                1 => await _telemetry.RequestOnlineModeAsync(structure.MacAddress, token, ct),
                _ => await _telemetry.RequestOfflineModeAsync(structure.MacAddress, token, ct)
            };
            if (!ok) return Refuse(503, "Could not queue the mode change. Try again.");
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { ok = true, pending = true });
            TempData["OfflineSuccess"] = "Mode change requested. Waiting for the controller to confirm.";
            return RedirectToAction("Index", "Dashboard", new { id = structureId });
        }

        // Queues a one-shot pH or nutrient dose for a structure and replies with its updated pending and last dose. The volume comes from the site's dose list, not from the page. Operators, workers and admins; changing the controller connection mode stays with operators and admins.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dose(string? structureId, string? dosingType, CancellationToken ct, bool manualPhOverride = false)
        {
            if (!User.CanControlEquipment())
            {
                return Refuse(StatusCodes.Status403Forbidden, "You do not have permission to dose.");
            }

            var option = DosingCatalog.Find(dosingType);

            if (option == null)
            {
                return Refuse(StatusCodes.Status400BadRequest, "That dose is not recognised.");
            }

            var accessToken = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, accessToken, ct);

            if (structure == null)
            {
                return Refuse(StatusCodes.Status404NotFound, "That structure could not be found.");
            }

            if (string.IsNullOrWhiteSpace(structure.MacAddress))
            {
                return Refuse(StatusCodes.Status400BadRequest, "This structure has no controller board linked yet.");
            }

            var outcome = await _telemetry.SendDoseAsync(structure.MacAddress, option.Type, option.VolumeMl, accessToken, ct, manualPhOverride);

            if (!outcome.Ok)
            {
                if (outcome.StatusCode >= 500 || outcome.StatusCode == 408)
                    return StatusCode(503, new { ok = false, outcomeUnknown = true, message = "The dose request outcome is unknown. Check its status before sending it again." });
                return Refuse(outcome.StatusCode, outcome.Message);
            }

            return Json(new
            {
                ok = true, message = "", pending = "Queued for controller: " + option.Label,
                last = (string?)null, hasPending = true, doseId = outcome.Id, deliveryStatus = "QUEUED"
            });
        }

        // Replies with a structure's current pending and last dose for the pH or nutrient page, so the page can watch a queued dose finish.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewDose(string? structureId, Guid doseId, bool pumpsCheckedOff,
            bool reservoirChecked, string notes, CancellationToken ct)
        {
            if (!User.CanManageSites()) return Forbid();
            var token = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, token, ct);
            if (structure == null || string.IsNullOrWhiteSpace(structure.MacAddress)) return NotFound();
            var result = await _telemetry.ReviewUnknownDoseAsync(structure.MacAddress, doseId,
                pumpsCheckedOff, reservoirChecked, notes, token, ct);
            return result.Ok ? Json(new { ok = true }) : Refuse(result.StatusCode, result.Message);
        }

        // Replies with a structure's current pending and last dose for the pH or nutrient page.
        [HttpGet]
        public async Task<IActionResult> DoseStatus(string? structureId, string? dosingType, CancellationToken ct, Guid? doseId = null, DateTime? requestedAfter = null)
        {
            var option = DosingCatalog.Find(dosingType);

            if (option == null)
            {
                return Refuse(StatusCodes.Status400BadRequest, "That dose is not recognised.");
            }

            var accessToken = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, accessToken, ct);

            if (structure == null || string.IsNullOrWhiteSpace(structure.MacAddress))
            {
                return Refuse(StatusCodes.Status404NotFound, "That structure could not be found.");
            }

            return await DoseStatusJson(structure.MacAddress, option.Group, accessToken, ct, doseId, requestedAfter);
        }

        // Loads a board's dosing history and builds the JSON reply holding the pending and last dose for one group of doses.
        private async Task<IActionResult> DoseStatusJson(string mac, string group, string accessToken, CancellationToken ct, Guid? doseId = null, DateTime? requestedAfter = null)
        {
            var history = await _telemetry.ReadDosingHistoryAsync(mac, accessToken, ct);
            if (!history.Available)
                return Refuse(502, "Dose status is temporarily unavailable. Checking again shortly.");
            var items = requestedAfter.HasValue && !doseId.HasValue ? history.Items.Where(x => x.CreatedAt >= requestedAfter.Value.ToUniversalTime().AddSeconds(-5)).ToList() : history.Items;
            if (requestedAfter.HasValue && !doseId.HasValue && !items.Any(x => DosingCatalog.InGroup(x.DosingType, group)))
                return Json(new { ok = true, pending = "Checking whether the request was saved", last = (string?)null, hasPending = true, deliveryStatus = "UNKNOWN" });
            var summary = SensorBindings.SummariseDoses(items, group, doseId);

            return Json(new { ok = true, message = "", pending = summary.Pending, last = summary.Last,
                hasPending = summary.HasPending, doseId = summary.DoseId, deliveryStatus = summary.Status, reason = summary.Reason });
        }

        // Builds the JSON reply for a command that was not sent or not accepted.
        private IActionResult Refuse(int statusCode, string message)
        {
            return StatusCode(statusCode, new { ok = false, on = false, message });
        }
    }
}
