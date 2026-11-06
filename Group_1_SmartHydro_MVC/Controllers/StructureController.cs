using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Observer;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class StructureController : Controller
    {
        private const string ManagerRoles = "Admin,Operator";

        private static readonly Dictionary<string, string> ApiFieldToFormField = new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = nameof(AddStructureViewModel.Name),
            ["controllerMac"] = nameof(AddStructureViewModel.MacAddress),
            ["cameraMac"] = nameof(AddStructureViewModel.CameraMac)
        };

        private readonly StructureApiClient _structures;
        private readonly ISiteContext _siteContext;
        private readonly ISiteChangePublisher _changes;

        public StructureController(StructureApiClient structures, ISiteContext siteContext, ISiteChangePublisher changes)
        {
            _structures = structures;
            _siteContext = siteContext;
            _changes = changes;
        }

        // Shows the current site's structures, plus its deleted structures for Admins who may purge them
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var model = new StructureListViewModel { CanManage = User.CanManageSites() };
            var site = await _siteContext.GetCurrentAsync();

            if (site == null)
            {
                return View(model);
            }

            var structures = await _structures.GetStructuresAsync(site.Id, User.GetAccessToken(), ct);

            if (structures == null)
            {
                model.LoadFailed = true;
                return View(model);
            }

            model.Structures = structures;

            if (User.IsInRole("Admin"))
            {
                var deleted = await _structures.GetDeletedStructuresAsync(site.Id, User.GetAccessToken(), ct);
                if (deleted != null)
                {
                    model.DeletedStructures = deleted;
                }
            }

            return View(model);
        }

        // Shows the empty form for adding a structure to the current site
        [Authorize(Roles = ManagerRoles)]
        [HttpGet]
        public async Task<IActionResult> Add()
        {
            if (await _siteContext.GetCurrentAsync() == null)
            {
                return RedirectToAction(nameof(Index));
            }

            return View(new AddStructureViewModel());
        }

        // Validates the new structure, asks the API to create it in the current site and returns to the list
        [Authorize(Roles = ManagerRoles)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(AddStructureViewModel model, CancellationToken ct)
        {
            var site = await _siteContext.GetCurrentAsync();
            if (site == null)
            {
                return RedirectToAction(nameof(Index));
            }

            if (!ValidateStructure(model, out var macAddress, out var cameraMac))
            {
                return View(model);
            }

            var input = new StructureInput(site.Id, model.Name!.Trim(), macAddress, model.Type, model.LabelColour!, cameraMac);
            var outcome = await _structures.CreateAsync(input, User.GetAccessToken(), ct);

            if (outcome.Result != StructureSaveResult.Saved)
            {
                AddApiError(outcome, "The structure could not be added. Please try again.");
                return View(model);
            }

            TempData["StructureAdded"] = input.Name;
            await _changes.PublishAsync(new SiteChange(site.Id, SiteChangeKind.StructureAdded, input.Name, User.GetEmail()), ct);

            return RedirectToAction(nameof(Index));
        }

        // Shows the structure form in edit mode, filled in with the structure's current details
        [Authorize(Roles = ManagerRoles)]
        [HttpGet]
        public async Task<IActionResult> Edit(string? id, CancellationToken ct)
        {
            var structure = await _structures.GetStructureAsync(id, User.GetAccessToken(), ct);
            if (structure == null)
            {
                return StructureNotFound();
            }

            return View("Add", BuildEditModel(structure));
        }

        // Validates the changes, asks the API to save them and returns to the list
        [Authorize(Roles = ManagerRoles)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string? id, AddStructureViewModel model, CancellationToken ct)
        {
            var existing = await _structures.GetStructureAsync(id, User.GetAccessToken(), ct);
            if (existing == null)
            {
                return StructureNotFound();
            }

            model.Id = existing.Id;
            model.CreatedAt = existing.CreatedAt;

            if (!ValidateStructure(model, out var macAddress, out var cameraMac))
            {
                return View("Add", model);
            }

            var input = new StructureInput(existing.SiteId, model.Name!.Trim(), macAddress, model.Type, model.LabelColour!, cameraMac);
            var outcome = await _structures.UpdateAsync(existing.Id, input, User.GetAccessToken(), ct);

            if (outcome.Result == StructureSaveResult.NotFound)
            {
                return StructureNotFound();
            }

            if (outcome.Result != StructureSaveResult.Saved)
            {
                AddApiError(outcome, "The structure could not be saved. Please try again.");
                return View("Add", model);
            }

            TempData["StructureUpdated"] = input.Name;
            await _changes.PublishAsync(new SiteChange(existing.SiteId, SiteChangeKind.StructureUpdated, input.Name, User.GetEmail()), ct);

            return RedirectToAction(nameof(Index));
        }

        // Soft-deletes a structure after the user confirms, offering an Undo link on the list
        [Authorize(Roles = ManagerRoles)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string? id, CancellationToken ct)
        {
            var structure = await _structures.GetStructureAsync(id, User.GetAccessToken(), ct);
            if (structure == null || !await _structures.DeleteAsync(structure.Id, User.GetAccessToken(), ct))
            {
                return StructureNotFound();
            }

            _siteContext.ForgetStructure(structure.Id);
            TempData["StructureDeleted"] = structure.Name;
            TempData["StructureDeletedId"] = structure.Id;
            await _changes.PublishAsync(new SiteChange(structure.SiteId, SiteChangeKind.StructureDeleted, structure.Name, User.GetEmail()), ct);

            return RedirectToAction(nameof(Index));
        }

        // Restores a soft-deleted structure in the current site
        [Authorize(Roles = ManagerRoles)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(string? id, CancellationToken ct)
        {
            var deleted = await FindDeletedAsync(id, ct);
            if (deleted == null)
            {
                return StructureNotFound();
            }

            var outcome = await _structures.RestoreAsync(deleted.Id, User.GetAccessToken(), ct);

            if (outcome.Result == StructureSaveResult.Conflict)
            {
                TempData["StructureRestoreError"] = outcome.Message;
                return RedirectToAction(nameof(Index));
            }

            if (outcome.Result != StructureSaveResult.Saved)
            {
                return StructureNotFound();
            }

            TempData["StructureRestored"] = deleted.Name;
            await _changes.PublishAsync(new SiteChange(deleted.SiteId, SiteChangeKind.StructureRestored, deleted.Name, User.GetEmail()), ct);

            return RedirectToAction(nameof(Index));
        }

        // Permanently purges a previously deleted structure (Admin only)
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Purge(string? id, CancellationToken ct)
        {
            var deleted = await FindDeletedAsync(id, ct);
            if (deleted == null || !await _structures.PurgeAsync(deleted.Id, User.GetAccessToken(), ct))
            {
                return StructureNotFound();
            }

            TempData["StructurePurged"] = deleted.Name;
            await _changes.PublishAsync(new SiteChange(deleted.SiteId, SiteChangeKind.StructurePurged, deleted.Name, User.GetEmail()), ct);

            return RedirectToAction(nameof(Index));
        }

        // Finds a deleted structure in the current site so its name can be shown after restoring or purging it
        private async Task<StructureViewModel?> FindDeletedAsync(string? id, CancellationToken ct)
        {
            var site = await _siteContext.GetCurrentAsync();
            if (site == null || string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var deleted = await _structures.GetDeletedStructuresAsync(site.Id, User.GetAccessToken(), ct);
            if (deleted == null)
            {
                return null;
            }

            return deleted.FirstOrDefault(s => s.Id == id);
        }

        // Checks the MAC address formats, name length and label colour
        private bool ValidateStructure(AddStructureViewModel model, out string macAddress, out string cameraMac)
        {
            macAddress = "";
            cameraMac = "";
            if (ModelState.GetFieldValidationState(nameof(model.MacAddress)) != ModelValidationState.Invalid)
            {
                if (!MacAddressFormat.TryNormalise(model.MacAddress, out macAddress))
                {
                    ModelState.AddModelError(nameof(model.MacAddress), "Enter a valid MAC address, e.g. A4:CF:12:9B:3E:01.");
                }
                else
                {
                    ModelState.Remove(nameof(model.MacAddress));
                    model.MacAddress = macAddress;
                }
            }

            if (!string.IsNullOrWhiteSpace(model.CameraMac)
                && ModelState.GetFieldValidationState(nameof(model.CameraMac)) != ModelValidationState.Invalid)
            {
                if (!MacAddressFormat.TryNormalise(model.CameraMac, out cameraMac))
                {
                    ModelState.AddModelError(nameof(model.CameraMac), "Enter a valid camera MAC address, e.g. A4:CF:12:9B:3E:02.");
                }
                else if (cameraMac == macAddress)
                {
                    ModelState.AddModelError(nameof(model.CameraMac), "The camera and the controller can't have the same MAC address.");
                }
                else
                {
                    ModelState.Remove(nameof(model.CameraMac));
                    model.CameraMac = cameraMac;
                }
            }

            var name = (model.Name ?? "").Trim();

            if (ModelState.GetFieldValidationState(nameof(model.Name)) != ModelValidationState.Invalid
                && (name.Length < 2 || name.Length > 60))
            {
                ModelState.AddModelError(nameof(model.Name), "The name must be between 2 and 60 characters.");
            }

            if (!StructureLabelColours.IsValid(model.LabelColour))
            {
                ModelState.AddModelError(nameof(model.LabelColour), "Please choose a label colour.");
                model.LabelColour = StructureLabelColours.Default;
            }

            return ModelState.IsValid;
        }

        // Shows the API's error beside the form field it belongs to, or at the top of the form
        private void AddApiError(StructureSaveOutcome outcome, string fallback)
        {
            var message = outcome.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = fallback;
            }

            var key = string.Empty;
            if (ApiFieldToFormField.TryGetValue(outcome.Field, out var formField))
            {
                key = formField;
            }

            ModelState.AddModelError(key, message);
        }

        // Builds the edit form model from a structure, falling back to the default label colour
        private static AddStructureViewModel BuildEditModel(StructureViewModel structure)
        {
            return new AddStructureViewModel
            {
                Id = structure.Id,
                MacAddress = structure.MacAddress,
                CameraMac = structure.CameraMac,
                Name = structure.Name,
                Type = structure.Type,
                LabelColour = StructureLabelColours.FromApi(structure.LabelColour),
                CreatedAt = structure.CreatedAt
            };
        }

        // Returns to the structure list with a message saying the structure could not be found
        private IActionResult StructureNotFound()
        {
            TempData["SelectStructure"] = "That structure could not be found. Please choose one from the list.";
            return RedirectToAction(nameof(Index));
        }
    }
}