using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Factory;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class DashboardController : Controller
    {
        private readonly StructureApiClient _structures;
        private readonly TelemetryApiClient _telemetry;
        private readonly ISiteContext _siteContext;
        private readonly IDashboardFactory _dashboards;

        public DashboardController(StructureApiClient structures, TelemetryApiClient telemetry, ISiteContext siteContext, IDashboardFactory dashboards)
        {
            _structures = structures;
            _telemetry = telemetry;
            _siteContext = siteContext;
            _dashboards = dashboards;
        }

        // Shows a structure's dashboard, reopening the last viewed structure when no id is given and switching to the structure's site
        public async Task<IActionResult> Index(string? id, string? source, CancellationToken ct)
        {
            var structureId = id;

            if (string.IsNullOrWhiteSpace(structureId))
            {
                var current = await _siteContext.GetCurrentAsync();

                if (current != null)
                {
                    structureId = _siteContext.LastViewedStructureId(current.Id);
                }
            }

            var structure = await _structures.GetStructureAsync(structureId, User.GetAccessToken(), ct);

            if (structure == null)
            {
                if (string.IsNullOrWhiteSpace(id) && string.Equals(source, "home", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["SelectStructure"] = "Please select a structure to see its readings.";
                }

                return RedirectToAction("Index", "Structure");
            }

            if (!string.IsNullOrEmpty(structure.SiteId))
            {
                _siteContext.Select(structure.SiteId);
                _siteContext.RememberStructure(structure.SiteId, structure.Id);
            }

            var readings = await _telemetry.GetDashboardReadingsAsync(structure, User.GetAccessToken(), ct);
            var model = _dashboards.Create(structure, readings);
            model.CanControl = User.CanControlEquipment();
            model.CanChangeMode = User.CanManageSites();

            return View(model);
        }

        // Shows the error page with the request id, never caching it
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}