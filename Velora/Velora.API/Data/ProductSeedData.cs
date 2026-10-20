using Velora.API.Models;

namespace Velora.API.Data;

internal static class ProductSeedData
{
    public static readonly List<Product> Products = new List<Product>
    {
        new Product
        {
            Id = 1,
            Sku = "APL-IP16-128",
            Name = "Apple iPhone 16 128GB",
            Brand = "Apple",
            Category = "Smartphones",
            Description = "Apple iPhone 16 with 6.1-inch Super Retina XDR display, A18 chip, 48MP Fusion camera and USB-C.",
            Price = 799.00m,
            Currency = "EUR",
            Rating = 4.8m,
            ReviewCount = 2456,
            StockQuantity = 35,
            Badge = "Best Seller",
            Icon = "📱",
            ImageFile = "iphone16.jpg"
        },
        new Product
        {
            Id = 2,
            Sku = "SMS-S25-256",
            Name = "Samsung Galaxy S25 256GB",
            Brand = "Samsung",
            Category = "Smartphones",
            Description = "Samsung Galaxy S25 with 6.2-inch Dynamic AMOLED display, Galaxy AI, powerful processor and triple camera.",
            Price = 899.99m,
            Currency = "EUR",
            Rating = 4.7m,
            ReviewCount = 1834,
            StockQuantity = 22,
            Badge = "Popular",
            Icon = "📱",
            ImageFile = "galaxy-s25.jpg"
        },
        new Product
        {
            Id = 3,
            Sku = "APL-MBA-M4-256",
            Name = "Apple MacBook Air 13 M4",
            Brand = "Apple",
            Category = "Laptops",
            Description = "13-inch MacBook Air powered by Apple M4 chip, 16GB unified memory, 256GB SSD and Liquid Retina display.",
            Price = 1199.00m,
            Currency = "EUR",
            Rating = 4.9m,
            ReviewCount = 1245,
            StockQuantity = 14,
            Badge = "Premium",
            Icon = "💻",
            ImageFile = "macbook-air-m4.jpg"
        },
        new Product
        {
            Id = 4,
            Sku = "DEL-XPS13-512",
            Name = "Dell XPS 13 Laptop",
            Brand = "Dell",
            Category = "Laptops",
            Description = "Premium ultrabook with 13.4-inch display, Intel Core Ultra processor, 16GB RAM and 512GB SSD.",
            Price = 1299.99m,
            Currency = "EUR",
            Rating = 4.6m,
            ReviewCount = 875,
            StockQuantity = 12,
            Badge = "New",
            Icon = "💻",
            ImageFile = "dell-xps13.jpg"
        },
        new Product
        {
            Id = 5,
            Sku = "SNY-WH1000XM5",
            Name = "Sony WH-1000XM5 Headphones",
            Brand = "Sony",
            Category = "Audio",
            Description = "Premium wireless noise-cancelling headphones with Bluetooth, multipoint connection and up to 30 hours battery life.",
            Price = 299.99m,
            Currency = "EUR",
            Rating = 4.8m,
            ReviewCount = 5620,
            StockQuantity = 48,
            Badge = "Best Seller",
            Icon = "🎧",
            ImageFile = "sony-wh1000xm5.jpg"
        },
        new Product
        {
            Id = 6,
            Sku = "APL-APPRO2",
            Name = "Apple AirPods Pro 2",
            Brand = "Apple",
            Category = "Audio",
            Description = "Wireless earbuds with active noise cancellation, adaptive audio, transparency mode and USB-C charging case.",
            Price = 249.00m,
            Currency = "EUR",
            Rating = 4.8m,
            ReviewCount = 7892,
            StockQuantity = 67,
            Badge = "Popular",
            Icon = "🎧",
            ImageFile = "airpods-pro2.jpg"
        },
        new Product
        {
            Id = 7,
            Sku = "SNY-PS5-SLIM",
            Name = "Sony PlayStation 5 Slim",
            Brand = "Sony",
            Category = "Gaming",
            Description = "PlayStation 5 Slim console with 1TB SSD, ultra-fast loading, ray tracing and DualSense wireless controller.",
            Price = 549.99m,
            Currency = "EUR",
            Rating = 4.9m,
            ReviewCount = 9345,
            StockQuantity = 19,
            Badge = "Best Seller",
            Icon = "🎮",
            ImageFile = "ps5-slim.jpg"
        },
        new Product
        {
            Id = 8,
            Sku = "LOG-MXMASTER3S",
            Name = "Logitech MX Master 3S",
            Brand = "Logitech",
            Category = "Accessories",
            Description = "Ergonomic wireless mouse with 8000 DPI sensor, quiet clicks, MagSpeed scrolling and multi-device connectivity.",
            Price = 109.99m,
            Currency = "EUR",
            Rating = 4.7m,
            ReviewCount = 4231,
            StockQuantity = 85,
            Badge = "Recommended",
            Icon = "🖱️",
            ImageFile = "logitech-mx-master3s.jpg"
        },
        new Product
        {
            Id = 9,
            Sku = "SMS-ODYSSEY-G5",
            Name = "Samsung Odyssey G5 27-inch",
            Brand = "Samsung",
            Category = "Monitors",
            Description = "27-inch QHD gaming monitor with 165Hz refresh rate, 1ms response time and AMD FreeSync Premium.",
            Price = 279.99m,
            Currency = "EUR",
            Rating = 4.6m,
            ReviewCount = 2156,
            StockQuantity = 28,
            Badge = "Gaming",
            Icon = "🖥️",
            ImageFile = "samsung-odyssey-g5.jpg"
        },
        new Product
        {
            Id = 10,
            Sku = "DYS-V15-DETECT",
            Name = "Dyson V15 Detect",
            Brand = "Dyson",
            Category = "Home Appliances",
            Description = "Cordless vacuum cleaner with dust illumination, intelligent suction adjustment, HEPA filtration and LCD display.",
            Price = 649.00m,
            Currency = "EUR",
            Rating = 4.7m,
            ReviewCount = 3128,
            StockQuantity = 16,
            Badge = "Premium",
            Icon = "🏠",
            ImageFile = "dyson-v15.jpg"
        }
    };
}