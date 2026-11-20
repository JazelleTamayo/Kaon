using Kaon.Data;
using Kaon.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Kaon.Controllers
{
    public class HomeController : Controller
    {
        private readonly KaonDbContext _db;
        public HomeController(KaonDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            // The landing page is for visitors who are logged out. Everyone else goes to their own area.
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return RedirectToAction("Index", "Admin");
                if (User.IsInRole("Owner")) return RedirectToAction("Index", "Owner");
                if (User.IsInRole("Customer")) return RedirectToAction("Index", "Menu");
            }

            // The price and trial length come from the Kaon admin's billing settings
            ViewBag.Plan = await _db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PlatformSettings();
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
