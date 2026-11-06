using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers;

[Authorize]
public class RealtimeController : Controller
{
    [HttpGet("/Realtime/Token")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Token()
    {
        var token = User.FindFirst("access_token")?.Value;
        return string.IsNullOrEmpty(token) ? Unauthorized() : Json(new { token });
    }
}
