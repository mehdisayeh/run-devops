using System.Diagnostics;
using Velora.Client.Services;
using Microsoft.AspNetCore.Mvc;
using Velora.Client.Models;

namespace Velora.Client.Controllers
{
    public class HomeController : Controller
    {
        private readonly ProductApiClient _products;

        public HomeController(ProductApiClient products)
        {
            _products = products;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            return View(await _products.GetProductsAsync(cancellationToken));
        }

        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
        {
            var product = await _products.GetProductAsync(id, cancellationToken);
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
