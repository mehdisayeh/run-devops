using System.Diagnostics;
using Velora.Client.Data;
using Microsoft.AspNetCore.Mvc;
using Velora.Client.Models;

namespace Velora.Client.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View(ProductContext.Products);
        }

        public IActionResult Details(int id)
        {
            var product = ProductContext.Products.FirstOrDefault(p => p.Id == id);
            return product is null ? NotFound() : View(product);
        }

        public IActionResult Cart() => View();

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
