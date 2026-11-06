using Microsoft.AspNetCore.Mvc;
using SmartHydro_UserGuide_MVC.Models;
using SmartHydro_UserGuide_MVC.Services;
using System.Diagnostics;

namespace SmartHydro_UserGuide_MVC.Controllers
{
    public class HomeController : Controller
    {
        private readonly GuideContent _content;

        public HomeController(GuideContent content)
        {
            _content = content;
        }

        // Shows the Start here page, the first page of the guide.
        public IActionResult Index()
        {
            return HelpPage("home");
        }

        // Shows the Getting set up page for new equipment.
        public IActionResult Setup()
        {
            return HelpPage("setup");
        }

        // Shows the Your account page about registering, joining a team and signing in.
        public IActionResult Account()
        {
            return HelpPage("account");
        }

        // Shows the Farm owners page for operators.
        public IActionResult Owners()
        {
            return HelpPage("owners");
        }

        // Shows the Farm workers page.
        public IActionResult Workers()
        {
            return HelpPage("workers");
        }

        // Shows the Fix a problem troubleshooting page.
        public IActionResult Troubleshoot()
        {
            return HelpPage("troubleshoot");
        }

        // Shows the Words explained glossary page.
        public IActionResult Words()
        {
            return HelpPage("words");
        }

        // Shows the error page with the request id so a problem can be traced in the logs.
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var requestId = HttpContext.TraceIdentifier;
            if (Activity.Current != null)
            {
                requestId = Activity.Current.Id;
            }

            return View(new ErrorViewModel { RequestId = requestId });
        }

        // Loads the page's content, sets the top bar title, and returns the view named after the calling action.
        private ViewResult HelpPage(string key)
        {
            var model = _content.GetPage(key);

            ViewData["Title"] = model.Title;

            return View(model);
        }
    }
}
