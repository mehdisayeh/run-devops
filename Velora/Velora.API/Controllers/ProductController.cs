using Microsoft.AspNetCore.Mvc;
using Velora.API.Data;
using Velora.API.Models;

namespace Velora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductController : ControllerBase
{
    private readonly ILogger<ProductController> _logger;
    private readonly IProductRepository _products;

    public ProductController(ILogger<ProductController> logger, IProductRepository products)
    {
        _logger = logger;
        _products = products;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetProducts(CancellationToken cancellationToken)
    {
        return Ok(await _products.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> GetProduct(int id, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            _logger.LogInformation("Product {ProductId} was not found", id);
            return NotFound();
        }

        return Ok(product);
    }
}
