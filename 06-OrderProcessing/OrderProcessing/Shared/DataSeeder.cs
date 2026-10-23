namespace OrderProcessing.Shared;

public sealed class DataSeeder(JsonFileStore store)
{
    public async Task ResetAsync(CancellationToken ct)
    {
        if (Directory.Exists(DataFiles.Root)) Directory.Delete(DataFiles.Root, recursive: true);
        Directory.CreateDirectory(DataFiles.Root);
        Directory.CreateDirectory(Path.Combine(DataFiles.Root, "invoices"));
        Directory.CreateDirectory(Path.Combine(DataFiles.Root, "reports"));

        var customers = new List<Customer>
        {
            C(1,"Ahmed Ali","Gold"), C(2,"Sara Mohamed","Silver"), C(3,"Omar Hassan","Bronze"), C(4,"Mona Ibrahim","Silver",false), C(5,"Youssef Adel","Gold"),
            C(6,"Nour Khaled","Bronze"), C(7,"Karim Samir","Silver"), C(8,"Laila Mahmoud","Gold"), C(9,"Hassan Tarek","Bronze"), C(10,"Dina Fathy","Silver")
        };
        var products = new List<Product>
        {
            P(1,"Laptop",1200m,10,2.2m), P(2,"Smartphone",800m,15,.4m), P(3,"Wireless Mouse",25m,2,.1m), P(4,"Mechanical Keyboard",90m,1,.9m),
            P(5,"USB-C Hub",45m,30,.2m), P(6,"Headphones",150m,12,.3m), P(7,"27\" Monitor",350m,6,5m), P(8,"Webcam",70m,20,.2m)
        };
        DateTime today = DateTime.Today;
        var coupons = new List<Coupon> { new(){Code="SAVE10",DiscountPercent=10,ExpiryDate=today.AddDays(180)}, new(){Code="WELCOME15",DiscountPercent=15,ExpiryDate=today.AddDays(90)}, new(){Code="FLASH20",DiscountPercent=20,ExpiryDate=today.AddDays(7)}, new(){Code="OLD20",DiscountPercent=20,ExpiryDate=today.AddDays(-30)} };
        int[][] items = [ [1,1,3,1], [2,1], [5,2,8,1], [6,1], [7,1], [5,1], [6,1,8,1], [1,1], [3,5], [4,1], [4,1], [2,1], [6,2], [8,2,5,1], [2,1], [7,1,5,1], [2,1], [6,1], [8,1], [1,1,6,1] ];
        int[] customerIds = [1,2,3,4,5,6,7,8,9,10,1,2,3,5,99,6,7,8,9,10];
        string?[] codes = ["SAVE10",null,"WELCOME15",null,null,"FLASH20",null,"SAVE10",null,null,null,"OLD20",null,"WELCOME15",null,null,"SAVE10","FAKE50",null,"FLASH20"];
        var incoming = new List<Order>();
        for (int i = 0; i < items.Length; i++)
        {
            var orderItems = new List<OrderItem>();
            for (int j = 0; j < items[i].Length; j += 2) orderItems.Add(new OrderItem { ProductId = items[i][j], Quantity = items[i][j + 1], UnitPrice = 0m });
            incoming.Add(new Order { Id=i+1, CustomerId=customerIds[i], Items=orderItems, CouponCode=codes[i], CreatedAt=DateTime.Now });
        }
        await Save(DataFiles.Customers, customers, ct);
        await Save(DataFiles.Products, products, ct);
        await Save(DataFiles.Coupons, coupons, ct);
        await Save(DataFiles.IncomingOrders, incoming, ct);
        await Save(DataFiles.Orders, new List<Order>(), ct);
        await Save(DataFiles.Payments, new List<PaymentTransaction>(), ct);
        await Save(DataFiles.Emails, new List<EmailMessage>(), ct);
        await Save(DataFiles.EmailRetry, new List<EmailMessage>(), ct);

        async Task Save<T>(string file, List<T> values, CancellationToken token)
        {
            await store.WriteAsync(file, values, token);
            Console.WriteLine($"[Seeder] {file} -> {values.Count} records");
        }
    }

    private static Customer C(int id,string name,string tier,bool active=true) => new(){Id=id,Name=name,Email=name.ToLowerInvariant().Replace(' ','.')+"@example.com",IsActive=active,LoyaltyTier=Enum.Parse<LoyaltyTier>(tier)};
    private static Product P(int id,string name,decimal price,int stock,decimal weight) => new(){Id=id,Name=name,Price=price,StockQuantity=stock,WeightKg=weight};
}
