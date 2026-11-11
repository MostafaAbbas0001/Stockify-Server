using Microsoft.Extensions.DependencyInjection;

namespace Stockify.Data;

/// <summary>
/// <c>dotnet run -- seed:demo</c>: builds three realistic demo companies (a clothing store, a
/// supermarket and a café) with users, catalogue, stock, promotions, customers, sales history,
/// a stock transfer and expenses. Everything goes through the same services the API uses, so the
/// demo data is shaped exactly like production data. Companies that already exist (by slug) are
/// skipped, so the command can be re-run safely. See docs/DEMO_DATA.md for the accounts.
/// </summary>
public static class DemoSeeder
{
    public const string DemoPassword = "Demo1234!";

    public static async Task RunAsync(IServiceProvider services)
    {
        Console.WriteLine("== Stockify demo seed ==");
        await ClothingStore.SeedAsync(services);
        await Supermarket.SeedAsync(services);
        await Cafe.SeedAsync(services);
        Console.WriteLine();
        Console.WriteLine($"Done. Every demo user signs in with the password {DemoPassword}");
        Console.WriteLine("Accounts: owner@ / manager@ / cashier@ at urbanthreads.demo, freshmart.demo and cornercafe.demo");
    }

    // ------------------------------------------------------------------ permission sets

    /// <summary>Everything a store manager does day to day: every permission except user administration and branch set-up.</summary>
    private static readonly Func<string, bool> ManagerPermissions = key =>
        !key.StartsWith("users.") && key != "branch.create" && key != "branch.delete" && key != "branch.all.access";

    private static readonly HashSet<string> CashierKeys = new(StringComparer.Ordinal)
    {
        "pos.tab.view", "pos.access", "orders.tab.view", "order.view", "order.create",
        "invoices.tab.view", "invoice.view", "invoice.payment.add",
        "customers.tab.view", "customer.view", "customer.create",
        "products.tab.view", "product.view", "variant.view", "category.view", "brand.view",
        "currency.view", "deliveryCharge.view", "employee.view", "stock.view"
    };

    /// <summary>A cashier: POS, orders, invoices and customers, read-only catalogue.</summary>
    private static readonly Func<string, bool> CashierPermissions = key => CashierKeys.Contains(key);

    // ------------------------------------------------------------------ clothing store

    private static class ClothingStore
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            await using var seed = await TenantSeed.StartAsync(services, new CreateTenantRequest
            {
                CompanyName = "Urban Threads",
                Slug = "urban-threads",
                BusinessType = BusinessTypeCodes.Retail,
                FirstBranchName = "Downtown Store",
                OwnerName = "Nadia Khoury",
                OwnerEmail = "owner@urbanthreads.demo",
                OwnerPassword = DemoPassword,
                BaseCurrency = new CurrencyRequest { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1 },
                TaxName = "VAT",
                TaxRate = 0.11m
            });
            if (seed is null) return;

            var downtown = seed.MainBranch;
            var mall = await seed.AddBranchAsync("Mall Store");
            var homeDelivery = await seed.AddDeliveryLocationAsync("Home Delivery", 5.00m);

            await seed.AddUserAsync("manager@urbanthreads.demo", "Omar Haddad", RoleCodes.OrderManager, ManagerPermissions, downtown.Id);
            await seed.AddUserAsync("cashier@urbanthreads.demo", "Lina Saad", RoleCodes.PointOfSales, CashierPermissions, downtown.Id);
            await seed.AddUserAsync("cashier2@urbanthreads.demo", "Rami Nassar", RoleCodes.PointOfSales, CashierPermissions, mall.Id);

            await seed.AddEmployeeAsync("Lina Saad", downtown.Id, isSale: true);
            await seed.AddEmployeeAsync("Omar Haddad", downtown.Id, isSale: true);
            await seed.AddEmployeeAsync("Rami Nassar", mall.Id, isSale: true);
            await seed.AddEmployeeAsync("Maya Fares", mall.Id, isSale: true);
            await seed.AddEmployeeAsync("Georges Abi Nader", downtown.Id, isSale: false);

            await seed.AddCustomersAsync("Sara Mansour", "Karim Haddad", "Rita Chamoun", "Ali Hamdan", "Nour Barakat",
                "Elie Saab", "Mira Khalil", "Jad Azar", "Lea Nassif", "Hadi Fakhoury", "Yasmina Daher", "Tony Sfeir");

            var size = await seed.AddAttributeAsync("Size", "XS", "S", "M", "L", "XL");
            var color = await seed.AddAttributeAsync("Color", "Black", "White", "Navy", "Red", "Olive", "Beige");

            var urban = await seed.AddBrandAsync("Urban Threads");
            var denim = await seed.AddBrandAsync("Denim & Co");
            var northwind = await seed.AddBrandAsync("Northwind");
            var solace = await seed.AddBrandAsync("Solace");

            var tshirts = await seed.AddCategoryAsync("T-Shirts");
            var shirts = await seed.AddCategoryAsync("Shirts");
            var jeans = await seed.AddCategoryAsync("Jeans");
            var jackets = await seed.AddCategoryAsync("Jackets");
            var dresses = await seed.AddCategoryAsync("Dresses");
            var accessories = await seed.AddCategoryAsync("Accessories");

            var winterSale = await seed.AddSaleAsync("Winter Sale", SaleTypeCodes.Percentage, 20m, startDaysAgo: 10, endDaysAhead: 30);
            var endOfSeason = await seed.AddSaleAsync("End of Season", SaleTypeCodes.FixedAmount, 10m, startDaysAgo: 20, endDaysAhead: 10);
            await seed.AddSaleAsync("Spring Preview", SaleTypeCodes.Percentage, 15m, startDaysAgo: -20, endDaysAhead: 50);

            var barcode = 1;
            string NextBarcode() => Ean13($"200{barcode++:D9}");

            // (brand, category, name, price, cost, colours, sizes, sale)
            var catalogue = new (Brand brand, Category category, string name, decimal price, decimal cost, string[] colours, string[] sizes, Sale? sale)[]
            {
                (urban, tshirts, "Classic Crew T-Shirt", 19.99m, 7.50m, new[] { "Black", "White", "Navy", "Red" }, new[] { "XS", "S", "M", "L", "XL" }, null),
                (urban, tshirts, "Graphic Tee", 24.99m, 9.00m, new[] { "Black", "White", "Olive" }, new[] { "S", "M", "L", "XL" }, null),
                (solace, shirts, "Oxford Button-Down", 49.99m, 20.00m, new[] { "White", "Navy" }, new[] { "S", "M", "L", "XL" }, null),
                (solace, shirts, "Linen Shirt", 54.99m, 22.00m, new[] { "White", "Beige", "Olive" }, new[] { "S", "M", "L" }, null),
                (denim, jeans, "Slim Fit Jeans", 69.99m, 28.00m, new[] { "Navy", "Black" }, new[] { "XS", "S", "M", "L", "XL" }, null),
                (denim, jeans, "Relaxed Jeans", 64.99m, 26.00m, new[] { "Navy" }, new[] { "S", "M", "L", "XL" }, null),
                (northwind, jackets, "Puffer Jacket", 129.99m, 55.00m, new[] { "Black", "Navy", "Olive" }, new[] { "S", "M", "L", "XL" }, winterSale),
                (northwind, jackets, "Denim Jacket", 89.99m, 38.00m, new[] { "Navy", "Black" }, new[] { "S", "M", "L" }, winterSale),
                (solace, dresses, "Wrap Dress", 79.99m, 32.00m, new[] { "Red", "Black", "Navy" }, new[] { "XS", "S", "M", "L" }, null),
                (solace, dresses, "Summer Dress", 59.99m, 24.00m, new[] { "White", "Beige" }, new[] { "XS", "S", "M", "L" }, endOfSeason),
                (urban, accessories, "Beanie", 14.99m, 5.00m, new[] { "Black", "Olive", "Red" }, Array.Empty<string>(), null),
                (urban, accessories, "Leather Belt", 34.99m, 14.00m, Array.Empty<string>(), new[] { "S", "M", "L" }, null),
                (northwind, accessories, "Wool Scarf", 29.99m, 12.00m, new[] { "Navy", "Beige" }, Array.Empty<string>(), null),
            };

            foreach (var item in catalogue)
            {
                var attributes = new List<ProductAttribute>();
                if (item.sizes.Length > 0) attributes.Add(size);
                if (item.colours.Length > 0) attributes.Add(color);
                var product = await seed.AddProductAsync(item.brand, item.category, item.name, attributes.ToArray());

                var sizeValues = item.sizes.Length > 0 ? item.sizes.Select(s => size.Values.Single(v => v.Value == s)).ToList() : new List<ProductAttributeValue> { null! };
                var colourValues = item.colours.Length > 0 ? item.colours.Select(c => color.Values.Single(v => v.Value == c)).ToList() : new List<ProductAttributeValue> { null! };

                foreach (var sizeValue in sizeValues)
                foreach (var colourValue in colourValues)
                {
                    var values = new[] { sizeValue, colourValue }.Where(v => v != null).ToList();
                    seed.AddVariant(product, values, item.price, item.cost, NextBarcode(), item.sale);
                }
            }
            await seed.Db.SaveChangesAsync();

            // Opening stock: every variant at Downtown, most at the Mall, a few deliberately empty or low.
            foreach (var variant in seed.Variants)
            {
                await seed.ReceiveAsync(variant, downtown.Id, seed.Next(3, 26), receivedAt: seed.DaysAgo(50));
                if (seed.Chance(0.8)) await seed.ReceiveAsync(variant, mall.Id, seed.Next(1, 16), receivedAt: seed.DaysAgo(50));
                if (seed.Chance(0.3)) seed.SetReorderLevel(variant, downtown.Id, 5);
            }
            await seed.Db.SaveChangesAsync();

            // 45 days of sales at both stores.
            var sold = new List<(int orderId, int invoiceId)>();
            for (var i = 0; i < 60; i++)
                sold.Add(await seed.SellAsync(downtown.Id, new Basket(1, 4, 1, 2), daysAgo: seed.Next(0, 45),
                    customer: seed.Chance(0.7) ? seed.RandomCustomer() : null,
                    discount: seed.Chance(0.1) ? (DiscountTypeCodes.Percent, 10m) : null,
                    payFraction: seed.Chance(0.08) ? 0.5m : 1m));
            for (var i = 0; i < 30; i++)
                sold.Add(await seed.SellAsync(mall.Id, new Basket(1, 3, 1, 2), daysAgo: seed.Next(0, 45),
                    customer: seed.Chance(0.5) ? seed.RandomCustomer() : null));

            // Three delivery orders: one still to ship, one in transit, one delivered.
            await seed.SellAsync(downtown.Id, new Basket(1, 2, 1, 1), 1, customer: seed.RandomCustomer(), deliveryLocationId: homeDelivery.Id, payFraction: 0);
            var inTransit = await seed.SellAsync(downtown.Id, new Basket(1, 2, 1, 1), 3, customer: seed.RandomCustomer(), deliveryLocationId: homeDelivery.Id, payFraction: 0);
            var delivered = await seed.SellAsync(downtown.Id, new Basket(1, 2, 1, 1), 6, customer: seed.RandomCustomer(), deliveryLocationId: homeDelivery.Id, payFraction: 0);
            await seed.Orders.UpdateOrderStatusAsync(inTransit.orderId, new UpdateOrderStatusRequest { Action = "ship" });
            await seed.Orders.UpdateOrderStatusAsync(delivered.orderId, new UpdateOrderStatusRequest { Action = "ship" });
            await seed.Orders.UpdateOrderStatusAsync(delivered.orderId, new UpdateOrderStatusRequest { Action = "deliver" });

            // Two returns (one whole line, one partial) and one cancelled unpaid order.
            await seed.ReturnAsync(sold[5].invoiceId, fullLine: true, reason: "Wrong size");
            await seed.ReturnAsync(sold[12].invoiceId, fullLine: false, reason: "Changed mind");
            var cancelled = await seed.SellAsync(downtown.Id, new Basket(1, 2, 1, 1), 2, payFraction: 0);
            await seed.Orders.UpdateOrderStatusAsync(cancelled.orderId, new UpdateOrderStatusRequest { Action = "cancel" });

            // A transfer Downtown → Mall that is still in transit (top the lots up first so the dispatch always has stock).
            var transferVariants = seed.Variants.Where(v => v.Product.Name == "Puffer Jacket").Take(3).ToList();
            foreach (var variant in transferVariants)
                await seed.ReceiveAsync(variant, downtown.Id, 10, receivedAt: seed.DaysAgo(2));
            await seed.Db.SaveChangesAsync();
            await seed.TransferAsync(downtown.Id, mall.Id, transferVariants, quantity: 2);

            await seed.AddMonthlyExpensesAsync(rent: 4500m, electricity: 320m, salaries: 1800m);
            seed.Done();
        }
    }

    // ------------------------------------------------------------------ supermarket

    private static class Supermarket
    {
        private sealed record Line(string Category, string[] Products, string[] Packs, string[] Brands, decimal MinPrice, decimal MaxPrice, int? ExpiryDays);

        private static readonly Line[] Lines =
        {
            new("Dairy & Eggs", new[] { "Full Fat Milk", "Low Fat Milk", "Greek Yogurt", "Natural Yogurt", "Butter", "Cheddar Cheese", "Cream Cheese", "Labneh", "Mozzarella", "Fresh Cream", "Eggs", "Halloumi" }, new[] { "500g", "1kg", "1L" }, new[] { "Almarai", "Danone", "Puck", "FreshMart" }, 1.20m, 9.50m, 14),
            new("Bakery", new[] { "White Bread", "Whole Wheat Bread", "Burger Buns", "Croissants", "Pita Bread", "Bagels", "Baguette", "Brioche", "Tortilla Wraps", "Rusks", "Brownies", "Cinnamon Rolls" }, new[] { "Single", "6-pack" }, new[] { "FreshMart", "Fine" }, 0.90m, 6.50m, 3),
            new("Meat & Poultry", new[] { "Chicken Breast", "Chicken Thighs", "Whole Chicken", "Minced Beef", "Beef Steak", "Lamb Chops", "Turkey Slices", "Beef Sausages", "Chicken Nuggets", "Salami", "Bacon", "Lamb Mince" }, new[] { "500g", "1kg" }, new[] { "Tyson", "FreshMart", "Fine" }, 4.50m, 24.00m, 6),
            new("Beverages", new[] { "Cola", "Diet Cola", "Orange Soda", "Lemon Lime Soda", "Sparkling Water", "Still Water", "Orange Juice", "Apple Juice", "Iced Tea", "Energy Drink", "Tonic Water", "Mango Nectar" }, new[] { "330ml", "500ml", "1.5L", "6-pack" }, new[] { "Coca-Cola", "Pepsi", "FreshMart" }, 0.50m, 6.00m, null),
            new("Snacks & Confectionery", new[] { "Potato Chips", "Tortilla Chips", "Salted Peanuts", "Chocolate Bar", "Cookies", "Wafers", "Popcorn", "Pretzels", "Gummy Bears", "Granola Bar", "Rice Cakes", "Crackers" }, new[] { "Single", "250g", "6-pack" }, new[] { "Lays", "Oreo", "Cadbury", "Kellogg's" }, 0.75m, 7.00m, null),
            new("Pantry & Dry Goods", new[] { "Spaghetti", "Penne", "Basmati Rice", "Lentils", "Chickpeas", "Olive Oil", "Sunflower Oil", "Ketchup", "Mayonnaise", "Tomato Paste", "Flour", "Sugar", "Instant Coffee", "Black Tea", "Corn Flakes", "Honey", "Peanut Butter", "Salt" }, new[] { "500g", "1kg", "2kg" }, new[] { "Barilla", "Heinz", "Nescafé", "Lipton", "FreshMart" }, 1.00m, 14.00m, null),
            new("Frozen", new[] { "Mixed Vegetables", "French Fries", "Green Peas", "Ice Cream Vanilla", "Ice Cream Chocolate", "Fish Fingers", "Pizza Margherita", "Spring Rolls", "Frozen Berries", "Chicken Burgers" }, new[] { "500g", "1kg" }, new[] { "Birds Eye", "Nestlé", "FreshMart" }, 2.50m, 12.00m, 180),
            new("Household & Cleaning", new[] { "Laundry Detergent", "Dishwashing Liquid", "Bleach", "Floor Cleaner", "Trash Bags", "Paper Towels", "Toilet Paper", "Sponges", "Glass Cleaner", "Air Freshener", "Aluminium Foil", "Cling Film" }, new[] { "Single", "1L", "2kg", "12-pack" }, new[] { "Ariel", "Fairy", "FreshMart" }, 1.50m, 18.00m, null),
            new("Personal Care", new[] { "Shampoo", "Conditioner", "Body Wash", "Bar Soap", "Toothpaste", "Toothbrush", "Deodorant", "Hand Cream", "Razors", "Cotton Pads", "Baby Diapers", "Baby Wipes" }, new[] { "Single", "250g", "500ml" }, new[] { "Dove", "Colgate", "Pampers", "FreshMart" }, 1.25m, 22.00m, null),
        };

        private static readonly (string name, decimal pricePerKg)[] Produce =
        {
            ("Bananas", 1.49m), ("Tomatoes", 2.29m), ("Cucumbers", 1.79m), ("Red Apples", 2.99m), ("Oranges", 1.99m),
            ("Potatoes", 1.19m), ("Onions", 0.99m), ("Grapes", 4.49m), ("Lemons", 2.49m), ("Carrots", 1.39m),
            ("Avocados", 5.99m), ("Strawberries", 6.49m), ("Bell Peppers", 3.29m), ("Lettuce", 1.89m), ("Garlic", 3.99m),
        };

        public static async Task SeedAsync(IServiceProvider services)
        {
            await using var seed = await TenantSeed.StartAsync(services, new CreateTenantRequest
            {
                CompanyName = "FreshMart Supermarket",
                Slug = "freshmart",
                BusinessType = BusinessTypeCodes.Grocery,
                FirstBranchName = "FreshMart Main",
                OwnerName = "Hassan Mroueh",
                OwnerEmail = "owner@freshmart.demo",
                OwnerPassword = DemoPassword,
                BaseCurrency = new CurrencyRequest { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1 },
                AdditionalCurrencies = { new CurrencyRequest { Code = "LBP", Name = "Lebanese Pound", Symbol = "ل.ل", ExchangeRate = 89500 } },
                TaxName = "VAT",
                TaxRate = 0.11m
            });
            if (seed is null) return;

            var main = seed.MainBranch;
            var homeDelivery = await seed.AddDeliveryLocationAsync("Home Delivery", 3.00m);

            await seed.AddUserAsync("manager@freshmart.demo", "Zeina Abdallah", RoleCodes.OrderManager, ManagerPermissions, main.Id);
            await seed.AddUserAsync("cashier@freshmart.demo", "Karim Jaber", RoleCodes.PointOfSales, CashierPermissions, main.Id);
            await seed.AddUserAsync("cashier2@freshmart.demo", "Dana Khalil", RoleCodes.PointOfSales, CashierPermissions, main.Id);
            await seed.AddUserAsync("stock@freshmart.demo", "Fadi Yazbeck", RoleCodes.StockManager, ManagerPermissions, main.Id);

            await seed.AddEmployeeAsync("Karim Jaber", main.Id, isSale: true);
            await seed.AddEmployeeAsync("Dana Khalil", main.Id, isSale: true);
            await seed.AddEmployeeAsync("Zeina Abdallah", main.Id, isSale: true);
            await seed.AddEmployeeAsync("Fadi Yazbeck", main.Id, isSale: false);

            await seed.AddCustomersAsync("Abir Haidar", "Walid Kassem", "Hanan Saleh", "Bassam Nehme", "Rana Itani",
                "Fouad Zein", "Nadine Awad", "Samer Ghanem");

            var pack = await seed.AddAttributeAsync("Pack", "Single", "250g", "500g", "1kg", "2kg", "330ml", "500ml", "1L", "1.5L", "6-pack", "12-pack");
            var brands = new Dictionary<string, Brand>();
            foreach (var name in Lines.SelectMany(l => l.Brands).Distinct())
                brands[name] = await seed.AddBrandAsync(name);
            var produceCategory = await seed.AddCategoryAsync("Fresh Produce");

            var snackSale = await seed.AddSaleAsync("Snack Attack", SaleTypeCodes.Percentage, 15m, startDaysAgo: 5, endDaysAhead: 9);
            var drinkSale = await seed.AddSaleAsync("Beverage Deal", SaleTypeCodes.FixedAmount, 0.50m, startDaysAgo: 3, endDaysAhead: 4);
            var dairySale = await seed.AddSaleAsync("Weekend Dairy", SaleTypeCodes.Percentage, 10m, startDaysAgo: 1, endDaysAhead: 2);
            await seed.AddSaleAsync("Back to School", SaleTypeCodes.Percentage, 20m, startDaysAgo: 90, endDaysAhead: -60);

            var barcode = 1;
            string NextBarcode() => Ean13($"6{barcode++:D11}");
            var expiryByVariant = new Dictionary<ProductVariant, int?>();

            foreach (var line in Lines)
            {
                var category = await seed.AddCategoryAsync(line.Category);
                foreach (var productName in line.Products)
                {
                    // Two brands per product so the supermarket has competing SKUs on the shelf.
                    foreach (var brandName in line.Brands.OrderBy(_ => seed.Next(0, 1000)).Take(2))
                    {
                        var brand = brands[brandName];
                        var packs = line.Packs.OrderBy(_ => seed.Next(0, 1000)).Take(seed.Next(2, 4)).ToArray();
                        var product = await seed.AddProductAsync(brand, category, productName, pack);
                        var basePrice = seed.Money(line.MinPrice, line.MaxPrice);
                        Sale? sale = null;
                        if (line.Category.StartsWith("Snacks") && brandName is "Lays" or "Oreo") sale = snackSale;
                        if (line.Category == "Beverages" && brandName is "Coca-Cola" or "Pepsi") sale = drinkSale;
                        if (line.Category.StartsWith("Dairy") && brandName == "Almarai") sale = dairySale;

                        for (var i = 0; i < packs.Length; i++)
                        {
                            var price = Math.Round(basePrice * (1 + 0.6m * i), 2);
                            var variant = seed.AddVariant(product, new[] { pack.Values.Single(v => v.Value == packs[i]) }, price, Math.Round(price * 0.65m, 2), NextBarcode(), sale);
                            expiryByVariant[variant] = line.ExpiryDays;
                        }
                    }
                }
            }

            // Weighed produce: sold per kilogram with scale barcodes (prefix 20, 5-digit PLU, 5 price digits, check).
            var freshMart = brands["FreshMart"];
            var plu = 1001;
            foreach (var (name, pricePerKg) in Produce)
            {
                var product = await seed.AddProductAsync(freshMart, produceCategory, $"{name} (per kg)");
                var variant = seed.AddVariant(product, Array.Empty<ProductAttributeValue>(), pricePerKg, Math.Round(pricePerKg * 0.6m, 2), Ean13($"20{plu++:D5}00000"), null);
                expiryByVariant[variant] = 7;
            }
            await seed.Db.SaveChangesAsync();
            Console.WriteLine($"  {seed.Variants.Count} SKUs");

            // Opening stock with batches and expiry dates where the category needs them.
            var lot = 1;
            foreach (var variant in seed.Variants)
            {
                var expiryDays = expiryByVariant.GetValueOrDefault(variant);
                if (expiryDays is null)
                {
                    await seed.ReceiveAsync(variant, main.Id, seed.Next(10, 200), receivedAt: seed.DaysAgo(40));
                }
                else
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    await seed.ReceiveAsync(variant, main.Id, seed.Next(5, 60), $"B{lot++:D5}", today.AddDays(seed.Next(1, expiryDays.Value + 1)), seed.DaysAgo(seed.Next(1, 10)));
                    // A second, fresher batch for half of them; a few expired leftovers that must never be sold.
                    if (seed.Chance(0.5)) await seed.ReceiveAsync(variant, main.Id, seed.Next(5, 40), $"B{lot++:D5}", today.AddDays(expiryDays.Value + seed.Next(1, 10)), seed.DaysAgo(1));
                    if (seed.Chance(0.1)) await seed.ReceiveAsync(variant, main.Id, seed.Next(1, 5), $"B{lot++:D5}", today.AddDays(-seed.Next(1, 5)), seed.DaysAgo(20));
                }
                if (seed.Chance(0.15)) seed.SetReorderLevel(variant, main.Id, 20);
            }
            await seed.Db.SaveChangesAsync();

            // 60 days of baskets.
            var sold = new List<(int orderId, int invoiceId)>();
            for (var i = 0; i < 240; i++)
            {
                var delivery = seed.Chance(0.05);
                sold.Add(await seed.SellAsync(main.Id, new Basket(2, 12, 1, 4), daysAgo: seed.Next(0, 60),
                    customer: delivery || seed.Chance(0.3) ? seed.RandomCustomer() : null,
                    deliveryLocationId: delivery ? homeDelivery.Id : null,
                    discount: seed.Chance(0.05) ? (DiscountTypeCodes.Amount, 2m) : null,
                    payMethodCode: seed.Chance(0.6) ? PaymentMethodCodes.Cash : PaymentMethodCodes.Card));
            }
            await seed.ReturnAsync(sold[10].invoiceId, fullLine: true, reason: "Damaged packaging");
            await seed.ReturnAsync(sold[40].invoiceId, fullLine: false, reason: "Expired on shelf");
            var cancelled = await seed.SellAsync(main.Id, new Basket(2, 4, 1, 2), 1, payFraction: 0);
            await seed.Orders.UpdateOrderStatusAsync(cancelled.orderId, new UpdateOrderStatusRequest { Action = "cancel" });

            await seed.AddMonthlyExpensesAsync(rent: 9000m, electricity: 2100m, salaries: 1400m);
            seed.Done();
        }
    }

    // ------------------------------------------------------------------ café

    private static class Cafe
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            await using var seed = await TenantSeed.StartAsync(services, new CreateTenantRequest
            {
                CompanyName = "Corner Café",
                Slug = "corner-cafe",
                BusinessType = BusinessTypeCodes.Cafe,
                FirstBranchName = "Hamra",
                OwnerName = "Joelle Rizk",
                OwnerEmail = "owner@cornercafe.demo",
                OwnerPassword = DemoPassword,
                BaseCurrency = new CurrencyRequest { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1 },
                AdditionalCurrencies = { new CurrencyRequest { Code = "LBP", Name = "Lebanese Pound", Symbol = "ل.ل", ExchangeRate = 89500 } },
                TaxName = "VAT",
                TaxRate = 0.11m
            });
            if (seed is null) return;

            var hamra = seed.MainBranch;
            await seed.AddUserAsync("manager@cornercafe.demo", "Tarek Sleiman", RoleCodes.OrderManager, ManagerPermissions, hamra.Id);
            await seed.AddUserAsync("cashier@cornercafe.demo", "Yara Hanna", RoleCodes.PointOfSales, CashierPermissions, hamra.Id);

            await seed.AddEmployeeAsync("Yara Hanna", hamra.Id, isSale: true);
            await seed.AddEmployeeAsync("Tarek Sleiman", hamra.Id, isSale: true);
            await seed.AddEmployeeAsync("Sami Khalaf", hamra.Id, isSale: true);

            await seed.AddCustomersAsync("Regular: Maria", "Regular: Khalil", "Office: Beirut Digital", "Regular: Anthony");

            var size = await seed.AddAttributeAsync("Size", "Small", "Medium", "Large");
            var milk = await seed.AddAttributeAsync("Milk", "Whole", "Skim", "Oat", "Almond");
            var flavour = await seed.AddAttributeAsync("Flavour", "Vanilla", "Caramel", "Hazelnut", "Blueberry", "Chocolate Chip", "Banana");

            var house = await seed.AddBrandAsync("Corner Café");
            var bottled = await seed.AddBrandAsync("Bottled");

            var hotCoffee = await seed.AddCategoryAsync("Hot Coffee");
            var coldDrinks = await seed.AddCategoryAsync("Cold Drinks");
            var tea = await seed.AddCategoryAsync("Tea");
            var pastries = await seed.AddCategoryAsync("Pastries");
            var food = await seed.AddCategoryAsync("Sandwiches");
            var extras = await seed.AddCategoryAsync("Extras");
            var retail = await seed.AddCategoryAsync("Retail");

            var happyHour = await seed.AddSaleAsync("Afternoon Happy Hour", SaleTypeCodes.Percentage, 25m, startDaysAgo: 7, endDaysAhead: 21);

            var barcode = 1;
            string NextBarcode() => Ean13($"300{barcode++:D9}");
            decimal SizePrice(decimal small, string sizeName) => sizeName switch { "Medium" => small + 0.50m, "Large" => small + 1.00m, _ => small };
            decimal MilkExtra(string milkName) => milkName is "Oat" or "Almond" ? 0.50m : 0m;

            // Drinks built from size × milk modifiers.
            var milkDrinks = new (string name, Category category, decimal small, Sale? sale)[]
            {
                ("Latte", hotCoffee, 4.00m, null), ("Cappuccino", hotCoffee, 4.00m, null), ("Flat White", hotCoffee, 4.25m, null),
                ("Mocha", hotCoffee, 4.75m, null), ("Iced Latte", coldDrinks, 4.50m, happyHour), ("Chai Latte", tea, 4.25m, null),
            };
            foreach (var drink in milkDrinks)
            {
                var product = await seed.AddProductAsync(house, drink.category, drink.name, size, milk);
                foreach (var s in size.Values)
                foreach (var m in milk.Values)
                    seed.AddVariant(product, new[] { s, m }, SizePrice(drink.small, s.Value) + MilkExtra(m.Value), 0.90m, NextBarcode(), drink.sale);
            }
            var sizedDrinks = new (string name, Category category, decimal small, Sale? sale)[]
            {
                ("Espresso", hotCoffee, 2.50m, null), ("Americano", hotCoffee, 3.00m, null), ("Hot Chocolate", hotCoffee, 4.00m, null),
                ("Iced Americano", coldDrinks, 3.50m, happyHour), ("Lemonade", coldDrinks, 3.75m, happyHour),
                ("Green Tea", tea, 3.00m, null), ("Earl Grey", tea, 3.00m, null), ("Chamomile", tea, 3.00m, null),
            };
            foreach (var drink in sizedDrinks)
            {
                var product = await seed.AddProductAsync(house, drink.category, drink.name, size);
                foreach (var s in size.Values)
                    seed.AddVariant(product, new[] { s }, SizePrice(drink.small, s.Value), 0.50m, NextBarcode(), drink.sale);
            }

            // Pastries and food: single variants, short shelf life.
            var perishables = new List<ProductVariant>();
            foreach (var (name, price) in new[] { ("Butter Croissant", 3.00m), ("Pain au Chocolat", 3.50m), ("Cinnamon Roll", 3.75m), ("Chocolate Cookie", 2.50m) })
            {
                var product = await seed.AddProductAsync(house, pastries, name);
                perishables.Add(seed.AddVariant(product, Array.Empty<ProductAttributeValue>(), price, 1.00m, NextBarcode(), null));
            }
            var muffin = await seed.AddProductAsync(house, pastries, "Muffin", flavour);
            foreach (var f in flavour.Values.Where(v => v.Value is "Blueberry" or "Chocolate Chip" or "Banana"))
                perishables.Add(seed.AddVariant(muffin, new[] { f }, 3.25m, 1.10m, NextBarcode(), null));
            foreach (var (name, price) in new[] { ("Turkey Club", 8.50m), ("Veggie Wrap", 7.50m), ("Halloumi Sandwich", 7.75m), ("Avocado Toast", 8.00m) })
            {
                var product = await seed.AddProductAsync(house, food, name);
                perishables.Add(seed.AddVariant(product, Array.Empty<ProductAttributeValue>(), price, 3.00m, NextBarcode(), null));
            }

            // Modifiers sold as extra lines.
            var extraShot = await seed.AddProductAsync(house, extras, "Extra Shot");
            seed.AddVariant(extraShot, Array.Empty<ProductAttributeValue>(), 0.75m, 0.20m, NextBarcode(), null);
            var syrup = await seed.AddProductAsync(house, extras, "Syrup Pump", flavour);
            foreach (var f in flavour.Values.Where(v => v.Value is "Vanilla" or "Caramel" or "Hazelnut"))
                seed.AddVariant(syrup, new[] { f }, 0.50m, 0.10m, NextBarcode(), null);
            var cream = await seed.AddProductAsync(house, extras, "Whipped Cream");
            seed.AddVariant(cream, Array.Empty<ProductAttributeValue>(), 0.50m, 0.10m, NextBarcode(), null);

            // Retail shelf.
            var retailItems = new List<ProductVariant>();
            foreach (var (brand, name, price, cost) in new[] { (house, "House Blend Beans 250g", 12.00m, 6.00m), (bottled, "Sparkling Water 330ml", 2.50m, 0.80m), (bottled, "Cola 330ml", 2.00m, 0.60m), (bottled, "Still Water 500ml", 1.00m, 0.25m) })
            {
                var product = await seed.AddProductAsync(brand, retail, name);
                retailItems.Add(seed.AddVariant(product, Array.Empty<ProductAttributeValue>(), price, cost, NextBarcode(), null));
            }
            await seed.Db.SaveChangesAsync();

            // Stock: drinks and extras are "always available" (large opening stock), pastries are a daily batch.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var variant in seed.Variants)
            {
                if (perishables.Contains(variant))
                    await seed.ReceiveAsync(variant, hamra.Id, seed.Next(8, 30), $"DAILY-{today:yyyyMMdd}", today.AddDays(2), seed.DaysAgo(0));
                else if (retailItems.Contains(variant))
                    await seed.ReceiveAsync(variant, hamra.Id, seed.Next(12, 48), receivedAt: seed.DaysAgo(20));
                else
                    await seed.ReceiveAsync(variant, hamra.Id, 500, receivedAt: seed.DaysAgo(30));
            }
            await seed.Db.SaveChangesAsync();

            // 30 days of tickets, mostly cash, small baskets with the occasional modifier line.
            for (var i = 0; i < 180; i++)
            {
                await seed.SellAsync(hamra.Id, new Basket(1, 4, 1, 2), daysAgo: seed.Next(0, 30),
                    customer: seed.Chance(0.15) ? seed.RandomCustomer() : null,
                    payMethodCode: seed.Chance(0.75) ? PaymentMethodCodes.Cash : PaymentMethodCodes.Card);
            }

            await seed.AddMonthlyExpensesAsync(rent: 2800m, electricity: 410m, salaries: 1100m);
            await seed.AddPurchaseExpenseAsync("Coffee beans (kg)", quantity: 20, unitCost: 18.00m, supplier: "Beirut Roasters");
            seed.Done();
        }
    }

    // ------------------------------------------------------------------ shared seeding helpers

    /// <summary>Shape of a random basket: how many distinct lines and how many units per line.</summary>
    private sealed record Basket(int MinLines, int MaxLines, int MinQty, int MaxQty);

    /// <summary>One demo company: a DI scope with the tenant selected, plus bookkeeping of what was created.</summary>
    private sealed class TenantSeed : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly IDisposable _tenantScope;
        private readonly Random _rng;
        private readonly int _ownerUserId;
        private readonly Dictionary<int, List<Employee>> _salesStaff = new();
        private readonly Dictionary<int, Dictionary<int, int>> _available = new();
        private readonly List<Customer> _customers = new();
        private readonly string _domain;
        private int _addManuallyId;

        public AppDbContext Db { get; }
        public ITenantContext TenantContext { get; }
        public LookupService Lookups { get; }
        public InventoryLotService Inventory { get; }
        public OrderService Orders { get; }
        public InvoiceService Invoices { get; }
        public UserService Users { get; }
        public ProductVariantSkuService Sku { get; }
        public ExpenseService Expenses { get; }
        public StockTransferService Transfers { get; }
        public Branch MainBranch { get; private set; } = null!;
        public int InStoreLocationId { get; private set; }
        public int WalkInCustomerId { get; private set; }
        public int BaseCurrencyId { get; private set; }
        public List<ProductVariant> Variants { get; } = new();

        private TenantSeed(IServiceScope scope, int tenantId, int ownerUserId, string domain)
        {
            _scope = scope;
            var sp = scope.ServiceProvider;
            Db = sp.GetRequiredService<AppDbContext>();
            TenantContext = sp.GetRequiredService<ITenantContext>();
            Lookups = sp.GetRequiredService<LookupService>();
            Inventory = sp.GetRequiredService<InventoryLotService>();
            Orders = sp.GetRequiredService<OrderService>();
            Invoices = sp.GetRequiredService<InvoiceService>();
            Users = sp.GetRequiredService<UserService>();
            Sku = sp.GetRequiredService<ProductVariantSkuService>();
            Expenses = sp.GetRequiredService<ExpenseService>();
            Transfers = sp.GetRequiredService<StockTransferService>();
            // Stays selected until DisposeAsync: every query and SaveChanges in this scope is for this company.
            _tenantScope = TenantContext.UseTenant(tenantId);
            _rng = new Random(tenantId * 7919);
            _ownerUserId = ownerUserId;
            _domain = domain;
        }

        /// <summary>Provisions the company exactly like POST /api/platform/tenants, or returns null when it already exists.</summary>
        public static async Task<TenantSeed?> StartAsync(IServiceProvider services, CreateTenantRequest request)
        {
            var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Tenants.AnyAsync(t => t.Slug == request.Slug))
            {
                Console.WriteLine($"- {request.CompanyName}: already exists, skipped");
                scope.Dispose();
                return null;
            }

            Console.WriteLine($"- {request.CompanyName}");
            var provisioning = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
            var created = await provisioning.CreateTenantAsync(request);
            var seed = new TenantSeed(scope, Prop<int>(created, "Id"), Prop<int>(created, "OwnerUserId"), request.OwnerEmail.Split('@')[1]);

            seed.MainBranch = await seed.Db.Branches.SingleAsync();
            seed.InStoreLocationId = await seed.Db.DeliveryLocations.Where(l => l.IsInStore).Select(l => l.Id).SingleAsync();
            seed.WalkInCustomerId = await seed.Db.Customers.Select(c => c.Id).SingleAsync();
            seed.BaseCurrencyId = await seed.Db.Currencies.Where(c => c.ExchangeRate == 1).Select(c => c.Id).FirstAsync();
            seed._addManuallyId = await seed.Lookups.IdAsync<MovementType>(MovementTypeCodes.AddManually);
            seed._salesStaff[seed.MainBranch.Id] = new List<Employee>();
            return seed;
        }

        public void Done() => Console.WriteLine($"  {Db.Orders.Local.Count} orders, {Variants.Count} variants, {_customers.Count} customers");

        public ValueTask DisposeAsync()
        {
            _tenantScope.Dispose();
            _scope.Dispose();
            return ValueTask.CompletedTask;
        }

        // ---- randomness
        public int Next(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
        public bool Chance(double probability) => _rng.NextDouble() < probability;
        public decimal Money(decimal min, decimal max) => Math.Round(min + (decimal)_rng.NextDouble() * (max - min), 2);
        public DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days).AddMinutes(-_rng.Next(0, 600));
        public Customer RandomCustomer() => _customers[_rng.Next(_customers.Count)];

        /// <summary>Available quantity per variant at a branch, loaded once and decremented as the seed sells.</summary>
        private async Task<Dictionary<int, int>> AvailableAsync(int branchId)
        {
            if (!_available.TryGetValue(branchId, out var quantities))
            {
                quantities = await Inventory.GetAvailableQuantitiesAsync(branchId, Variants.Select(v => v.Id).ToList());
                _available[branchId] = quantities;
            }
            return quantities;
        }

        /// <summary>Random distinct variants that are in stock at the branch, with quantities the stock can cover.</summary>
        private async Task<List<(ProductVariant variant, int quantity)>> RandomBasketAsync(int branchId, Basket basket)
        {
            var available = await AvailableAsync(branchId);
            var lines = _rng.Next(basket.MinLines, basket.MaxLines + 1);
            var picked = Variants
                .Where(v => available.GetValueOrDefault(v.Id) > 0)
                .OrderBy(_ => _rng.Next())
                .Take(lines)
                .Select(v => (v, Math.Min(_rng.Next(basket.MinQty, basket.MaxQty + 1), available[v.Id])))
                .ToList();
            if (picked.Count == 0)
                throw new InvalidOperationException($"Branch {branchId} has no stock left to sell.");
            return picked;
        }

        // ---- organisation
        public async Task<Branch> AddBranchAsync(string name)
        {
            var branch = new Branch { Name = name, CreatedAt = DateTime.UtcNow };
            Db.Branches.Add(branch);
            await Db.SaveChangesAsync();
            _salesStaff[branch.Id] = new List<Employee>();
            return branch;
        }

        public async Task<DeliveryLocation> AddDeliveryLocationAsync(string name, decimal fee)
        {
            var location = new DeliveryLocation { LocationName = name, Fee = fee, CreatedAt = DateTime.UtcNow };
            Db.DeliveryLocations.Add(location);
            await Db.SaveChangesAsync();
            return location;
        }

        public async Task<User> AddUserAsync(string email, string name, string roleCode, Func<string, bool> allowPermission, int branchId)
        {
            var roleId = await Db.Roles.Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync();
            var user = await Users.CreateUserEntityAsync(new CreateUserRequest
            {
                Email = email, Username = name, Password = DemoPassword, RoleId = roleId, BranchId = branchId
            });
            var rows = await Db.UserPermissions.Include(up => up.Permission).Where(up => up.UserId == user.Id).ToListAsync();
            foreach (var row in rows)
                row.IsActive = allowPermission(row.Permission.Key);
            await Db.SaveChangesAsync();
            return user;
        }

        public async Task<Employee> AddEmployeeAsync(string name, int branchId, bool isSale)
        {
            var employee = new Employee
            {
                Name = name,
                BranchId = branchId,
                IsSale = isSale,
                Email = $"{name.ToLowerInvariant().Replace(' ', '.')}@{_domain}",
                Phone = $"+961 3 {_rng.Next(100000, 999999)}",
                Address = ""
            };
            Db.Employees.Add(employee);
            await Db.SaveChangesAsync();
            if (isSale) _salesStaff[branchId].Add(employee);
            return employee;
        }

        public async Task AddCustomersAsync(params string[] names)
        {
            foreach (var name in names)
            {
                var customer = new Customer
                {
                    Name = name,
                    Phone = $"+961 7{_rng.Next(1000000, 9999999)}",
                    Email = $"{name.ToLowerInvariant().Replace(": ", ".").Replace(' ', '.')}@example.com",
                    Address = $"{_rng.Next(1, 200)} {new[] { "Hamra St", "Verdun St", "Gemmayze", "Achrafieh", "Mar Mikhael", "Badaro" }[_rng.Next(6)]}, Beirut",
                    CreatedAt = DaysAgo(_rng.Next(30, 400))
                };
                Db.Customers.Add(customer);
                _customers.Add(customer);
            }
            await Db.SaveChangesAsync();
        }

        // ---- catalogue
        public async Task<ProductAttribute> AddAttributeAsync(string name, params string[] values)
        {
            var attribute = new ProductAttribute { Name = name };
            foreach (var value in values)
                attribute.Values.Add(new ProductAttributeValue { Value = value, ProductAttribute = attribute });
            Db.ProductAttributes.Add(attribute);
            await Db.SaveChangesAsync();
            return attribute;
        }

        public async Task<Brand> AddBrandAsync(string name)
        {
            var brand = new Brand { Name = name };
            Db.Brands.Add(brand);
            await Db.SaveChangesAsync();
            return brand;
        }

        public async Task<Category> AddCategoryAsync(string name)
        {
            var category = new Category { Name = name };
            Db.Categories.Add(category);
            await Db.SaveChangesAsync();
            return category;
        }

        public async Task<Product> AddProductAsync(Brand brand, Category category, string name, params ProductAttribute[] attributes)
        {
            var product = new Product { Name = name, Brand = brand, BrandId = brand.Id, Category = category, CategoryId = category.Id, CreatedAt = DaysAgo(_rng.Next(60, 120)) };
            foreach (var attribute in attributes)
                product.Attributes.Add(attribute);
            Db.Products.Add(product);
            await Db.SaveChangesAsync();
            return product;
        }

        /// <summary>Adds a variant the way ProductVariantService.CreateAsync does (SKU, default lot, a LocationStock per branch). Not saved yet.</summary>
        public ProductVariant AddVariant(Product product, IReadOnlyList<ProductAttributeValue> values, decimal price, decimal cost, string barcode, Sale? sale)
        {
            var sku = Sku.BuildSku(product.Brand.Name, product.Name, values);
            var variant = new ProductVariant
            {
                Product = product,
                ProductId = product.Id,
                SKU = sku,
                Price = price,
                NetPrice = cost,
                Barcode = barcode,
                SaleId = sale?.Id,
                CreatedAt = product.CreatedAt,
                IsSellable = true
            };
            foreach (var value in values)
                variant.AttributeValues.Add(value);
            variant.InventoryLots.Add(new InventoryLot { LotNumber = InventoryLotService.BuildDefaultLotNumber(sku), ReceivedAt = product.CreatedAt });
            foreach (var branchId in _salesStaff.Keys)
                variant.LocationStocks.Add(new LocationStock { BranchId = branchId, ReorderLevel = 0, LastUpdated = DateTime.UtcNow });
            Db.ProductVariants.Add(variant);
            Variants.Add(variant);
            return variant;
        }

        public void SetReorderLevel(ProductVariant variant, int branchId, int level)
        {
            var location = variant.LocationStocks.First(l => l.BranchId == branchId);
            location.ReorderLevel = level;
        }

        public async Task<Sale> AddSaleAsync(string name, string typeCode, decimal value, int startDaysAgo, int endDaysAhead)
        {
            var sale = new Sale
            {
                Name = name,
                SaleTypeId = await Lookups.IdAsync<SaleType>(typeCode),
                Value = value,
                StartDate = DateTime.UtcNow.Date.AddDays(-startDaysAgo),
                EndDate = DateTime.UtcNow.Date.AddDays(endDaysAhead),
                CreatedAt = DateTime.UtcNow.AddDays(-Math.Max(startDaysAgo, 1))
            };
            Db.Sales.Add(sale);
            await Db.SaveChangesAsync();
            return sale;
        }

        // ---- stock
        /// <summary>Receives stock into a lot through InventoryLotService and writes the ADD_MANUALLY movement. Call SaveChanges afterwards.</summary>
        public async Task ReceiveAsync(ProductVariant variant, int branchId, int quantity, string? lotNumber = null, DateOnly? expiry = null, DateTime? receivedAt = null)
        {
            var when = receivedAt ?? DateTime.UtcNow;
            InventoryLot lot;
            if (lotNumber is null)
            {
                lot = variant.InventoryLots.First();
            }
            else
            {
                var existing = variant.InventoryLots.FirstOrDefault(l => l.LotNumber == lotNumber);
                if (existing is null)
                {
                    existing = new InventoryLot { ProductVariant = variant, ProductVariantId = variant.Id, LotNumber = lotNumber, ExpiryDate = expiry, ReceivedAt = when };
                    variant.InventoryLots.Add(existing);
                    Db.InventoryLots.Add(existing);
                    await Db.SaveChangesAsync();
                }
                lot = existing;
            }

            await Inventory.AddToLotAsync(branchId, lot, quantity);
            Db.StockMovements.Add(new StockMovement
            {
                ProductVariantId = variant.Id,
                InventoryLotId = lot.Id,
                BranchId = branchId,
                MovementTypeId = _addManuallyId,
                Quantity = quantity,
                Reason = "Opening stock",
                CreatedAt = when
            });
        }

        // ---- sales history
        /// <summary>Places an order through OrderService, pays it through InvoiceService, then back-dates it.</summary>
        public async Task<(int orderId, int invoiceId)> SellAsync(
            int branchId,
            Basket basket,
            int daysAgo,
            Customer? customer = null,
            int? deliveryLocationId = null,
            (string code, decimal value)? discount = null,
            string payMethodCode = PaymentMethodCodes.Cash,
            decimal payFraction = 1m)
        {
            var items = await RandomBasketAsync(branchId, basket);
            var employees = _salesStaff[branchId];
            var employee = employees[_rng.Next(employees.Count)];
            var dto = new CreateOrderRequest
            {
                CustomerId = customer?.Id ?? WalkInCustomerId,
                DeliveryLocationId = deliveryLocationId ?? InStoreLocationId,
                CreatedByEmployeeId = employee.Id,
                DiscountTypeId = discount is null ? null : await Lookups.IdAsync<DiscountType>(discount.Value.code),
                DiscountValue = discount?.value ?? 0m,
                Items = items.Select(i => new CreateOrderItemRequest { ProductVariantId = i.variant.Id, Quantity = i.quantity }).ToList()
            };

            var result = await Orders.PlaceOrderAsync(dto, branchId);
            var available = await AvailableAsync(branchId);
            foreach (var (variant, quantity) in items)
                available[variant.Id] -= quantity;
            var orderId = Prop<int>(result, "orderId");
            var invoiceId = Prop<int>(result, "invoiceId");
            var when = DaysAgo(daysAgo);

            var invoice = await Db.Invoices.SingleAsync(i => i.Id == invoiceId);
            if (payFraction > 0)
            {
                var amount = Math.Round(invoice.TotalAmount * payFraction, 2, MidpointRounding.AwayFromZero);
                if (amount > 0)
                    await Invoices.AddPaymentAsync(invoiceId, new RecordPaymentRequest
                    {
                        Amount = amount,
                        PaymentMethodId = await Lookups.IdAsync<PaymentMethod>(payMethodCode),
                        CurrencyId = BaseCurrencyId,
                        Reference = payMethodCode == PaymentMethodCodes.Card ? $"AUTH-{_rng.Next(100000, 999999)}" : string.Empty
                    });
            }

            var order = await Db.Orders.SingleAsync(o => o.Id == orderId);
            order.CreatedAt = when;
            invoice.IssuedAt = when;
            foreach (var payment in Db.Payments.Local.Where(p => p.InvoiceId == invoiceId))
                payment.PaidAt = when;
            foreach (var movement in Db.StockMovements.Local.Where(m => m.Reason == $"Order #{orderId}"))
                movement.CreatedAt = when;
            await Db.SaveChangesAsync();
            return (orderId, invoiceId);
        }

        public async Task ReturnAsync(int invoiceId, bool fullLine, string reason)
        {
            var line = await Db.InvoiceItems.Where(i => i.InvoiceId == invoiceId).OrderBy(i => i.Id).FirstAsync();
            var quantity = fullLine ? line.Quantity : Math.Max(1, line.Quantity / 2);
            await Invoices.ReturnInvoiceAsync(invoiceId, new CreateInvoiceReturnRequest
            {
                Reason = reason,
                Items = new List<InvoiceReturnItemRequest> { new() { InvoiceItemId = line.Id, Quantity = quantity, Reason = reason } }
            });
        }

        public async Task TransferAsync(int sourceBranchId, int destinationBranchId, IReadOnlyList<ProductVariant> variants, int quantity)
        {
            var created = await Transfers.CreateTransferAsync(new CreateStockTransferRequest
            {
                SourceBranchId = sourceBranchId,
                DestinationBranchId = destinationBranchId,
                Notes = "Rebalancing winter stock",
                Items = variants.Select(v => new CreateStockTransferItemRequest { ProductVariantId = v.Id, Quantity = quantity }).ToList()
            }, _ownerUserId);
            var transferId = Prop<int>(created, "Id");
            var items = await Db.StockTransferItems.Where(i => i.StockTransferId == transferId).ToListAsync();
            await Transfers.DispatchTransferAsync(transferId, new DispatchStockTransferRequest
            {
                SourceBranchId = sourceBranchId,
                Lots = items.Select(i => new DispatchStockTransferLotRequest
                {
                    StockTransferItemId = i.Id,
                    InventoryLotId = variants.Single(v => v.Id == i.ProductVariantId).InventoryLots.First().Id,
                    Quantity = quantity
                }).ToList()
            });
        }

        // ---- expenses
        public async Task AddMonthlyExpensesAsync(decimal rent, decimal electricity, decimal salaries)
        {
            var rentCategory = await CategoryIdAsync("4101");
            var electricityCategory = await CategoryIdAsync("4102");
            var salaryCategory = await CategoryIdAsync("1100");
            var fixedType = await Lookups.IdAsync<ExpenseLineType>(ExpenseLineTypeCodes.Fixed);
            var amountType = await Lookups.IdAsync<ExpenseLineType>(ExpenseLineTypeCodes.Amount);
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            await CreateExpenseAsync(await CategoryIdAsync("4000"), monthStart, new List<CreateExpenseLineRequest>
            {
                new() { CategoryId = rentCategory, Amount = rent, LineTypeId = fixedType, Rate = rent, Note = "Monthly rent", ExpensePeriodStart = monthStart, ExpensePeriodEnd = monthStart.AddMonths(1).AddDays(-1), BranchId = MainBranch.Id },
                new() { CategoryId = electricityCategory, Amount = electricity, LineTypeId = amountType, Note = "Electricity bill", ReferenceNumber = $"EDL-{_rng.Next(10000, 99999)}", BranchId = MainBranch.Id }
            });

            var staff = _salesStaff.Values.SelectMany(s => s).ToList();
            await CreateExpenseAsync(await CategoryIdAsync("1000"), monthStart, staff.Select(employee => new CreateExpenseLineRequest
            {
                CategoryId = salaryCategory, Amount = salaries, LineTypeId = fixedType, Rate = salaries, EmployeeId = employee.Id,
                Note = "Monthly salary", ExpensePeriodStart = monthStart, ExpensePeriodEnd = monthStart.AddMonths(1).AddDays(-1), BranchId = employee.BranchId
            }).ToList());
        }

        public async Task AddPurchaseExpenseAsync(string item, int quantity, decimal unitCost, string supplier)
        {
            await CreateExpenseAsync(await CategoryIdAsync("4000"), DateTime.UtcNow.AddDays(-3), new List<CreateExpenseLineRequest>
            {
                new()
                {
                    CategoryId = await CategoryIdAsync("4113"), Amount = quantity * unitCost,
                    LineTypeId = await Lookups.IdAsync<ExpenseLineType>(ExpenseLineTypeCodes.Quantity),
                    Quantity = quantity, UnitCost = unitCost, ItemName = item, SupplierName = supplier, PurchaseDate = DateTime.UtcNow.AddDays(-3), BranchId = MainBranch.Id
                }
            });
        }

        private async Task CreateExpenseAsync(int categoryId, DateTime date, List<CreateExpenseLineRequest> lines)
        {
            var created = await Expenses.CreateExpenseAsync(new CreateExpenseRequest { ExpenseDate = date, ExpenseCategoryId = categoryId, Lines = lines }, _ownerUserId);
            var expense = await Db.Expenses.SingleAsync(e => e.Id == Prop<int>(created, "id"));
            expense.CreatedAt = date.AddDays(2);
            await Db.SaveChangesAsync();
        }

        private Task<int> CategoryIdAsync(string code) => Db.ExpenseCategories.Where(c => c.Code == code).Select(c => c.Id).SingleAsync();
    }

    // ------------------------------------------------------------------ small helpers

    private static T Prop<T>(object source, string name) =>
        (T)(source.GetType().GetProperty(name)?.GetValue(source)
            ?? throw new InvalidOperationException($"Result has no '{name}' property."));

    /// <summary>Appends the EAN-13 check digit to 12 digits.</summary>
    public static string Ean13(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || !twelveDigits.All(char.IsDigit))
            throw new ArgumentException("EAN-13 needs 12 digits.", nameof(twelveDigits));
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = twelveDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        return twelveDigits + (10 - sum % 10) % 10;
    }
}
