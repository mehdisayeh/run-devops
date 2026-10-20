using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Velora.API.Controllers;
using Velora.API.Data;
using Velora.API.Models;

namespace Velora.API.Tests;

public class ProductControllerTests
{
    [Fact]
    public async Task GetProductsReturnsRepositoryProductsAndPassesCancellationToken()
    {
        using var source = new CancellationTokenSource();
        IReadOnlyList<Product> products = [new() { Id = 42, Name = "Database product", Price = 12.34m }];
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetAllAsync(source.Token)).ReturnsAsync(products);
        var controller = new ProductController(NullLogger<ProductController>.Instance, repository.Object);

        var response = await controller.GetProducts(source.Token);

        Assert.Same(products, Assert.IsType<OkObjectResult>(response.Result).Value);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetProductsReturnsEmptyArrayWhenCollectionIsEmpty()
    {
        var repository = new Mock<IProductRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Product>());
        var controller = new ProductController(NullLogger<ProductController>.Instance, repository.Object);

        var response = await controller.GetProducts(CancellationToken.None);

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Product>>(Assert.IsType<OkObjectResult>(response.Result).Value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetProductReturnsProductOrNotFound(bool exists)
    {
        using var source = new CancellationTokenSource();
        var product = exists ? new Product { Id = 42, Price = 12.34m } : null;
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetByIdAsync(42, source.Token)).ReturnsAsync(product);
        var controller = new ProductController(NullLogger<ProductController>.Instance, repository.Object);

        var response = await controller.GetProduct(42, source.Token);

        if (exists) Assert.Same(product, Assert.IsType<OkObjectResult>(response.Result).Value);
        else Assert.IsType<NotFoundResult>(response.Result);
        repository.VerifyAll();
    }
}
