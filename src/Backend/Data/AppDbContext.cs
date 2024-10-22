using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Polly;

namespace eShopSupport.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<Product> Products { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Ticket>().HasMany(t => t.Messages).WithOne().OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Ticket>().HasOne(t => t.Product);
        }

        public static async Task EnsureDbCreatedAsync(IServiceProvider services, string? initialImportDataDir)
        {
            using var scope = services.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Wait until the DB is ready
            var retryPolicy = Policy.Handle<Exception>().RetryAsync(3, onRetry: (exception, retryCount) =>
            {
                Console.WriteLine($"Retry {retryCount} for exception {exception.Message}");
            });

            var timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromSeconds(30));
            var circuitBreakerPolicy = Policy.Handle<Exception>().CircuitBreakerAsync(2, TimeSpan.FromMinutes(1));

            var resiliencePipeline = Policy.WrapAsync(retryPolicy, timeoutPolicy, circuitBreakerPolicy);

            // Use Polly Context and CancellationToken
            var createdDb = await resiliencePipeline.ExecuteAsync((context, ct) => dbContext.Database.EnsureCreatedAsync(ct), new Context(), CancellationToken.None);

            if (createdDb && !string.IsNullOrEmpty(initialImportDataDir))
            {
                await ImportInitialData(dbContext, initialImportDataDir);
            }
        }

        private static async Task ImportInitialData(AppDbContext dbContext, string dirPath)
        {
            try
            {
                var customers = JsonSerializer.Deserialize<Customer[]>(File.ReadAllText(Path.Combine(dirPath, "customers.json")))!;
                var categories = JsonSerializer.Deserialize<ProductCategory[]>(File.ReadAllText(Path.Combine(dirPath, "categories.json")))!;
                var products = JsonSerializer.Deserialize<Product[]>(File.ReadAllText(Path.Combine(dirPath, "products.json")))!;
                var tickets = JsonSerializer.Deserialize<Ticket[]>(File.ReadAllText(Path.Combine(dirPath, "tickets.json")))!;

                // Remove the IDs to allow auto-generation by the DB
                foreach (var ticket in tickets)
                {
                    ticket.TicketId = 0;
                    ticket.Customer = customers.First(c => c.CustomerId == ticket.CustomerId);
                    ticket.CreatedAt = DateTime.UtcNow;
                    foreach (var message in ticket.Messages)
                    {
                        message.MessageId = 0;
                        message.CreatedAt = DateTime.UtcNow;
                    }
                }
                foreach (var customer in customers)
                {
                    customer.CustomerId = 0;
                }

                await dbContext.Customers.AddRangeAsync(customers);
                await dbContext.ProductCategories.AddRangeAsync(categories);
                await dbContext.Products.AddRangeAsync(products);
                await dbContext.Tickets.AddRangeAsync(tickets);
                await dbContext.SaveChangesAsync();
            }
            catch
            {
                // If the import fails, drop the DB and retry later
                await dbContext.Database.EnsureDeletedAsync();
                throw;
            }
        }
    }
}
