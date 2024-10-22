using eShopSupport.Backend.Api;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.PythonInference;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using SmartComponents.LocalEmbeddings.SemanticKernel;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("backenddb");

builder.AddQdrantHttpClient("vector-db");
builder.Services.AddScoped(s => new QdrantMemoryStore(
    s.GetQdrantHttpClient("vector-db"), 384));

builder.Services.AddScoped<IMemoryStore>(s => s.GetRequiredService<QdrantMemoryStore>());
builder.Services.AddScoped<ITextEmbeddingGenerationService, LocalTextEmbeddingGenerationService>();
builder.Services.AddScoped<ISemanticTextMemory, SemanticTextMemory>();
builder.Services.AddScoped<ProductSemanticSearch>();
builder.Services.AddScoped<ProductManualSemanticSearch>();
builder.Services.AddScoped<TicketSummarizer>();

// Configure HttpClient with Polly for PythonInferenceClient
builder.Services.AddHttpClient<PythonInferenceClient>(c =>
{
    c.BaseAddress = new Uri("http://python-inference");
    c.Timeout = TimeSpan.FromSeconds(25);  // Ensure HttpClient timeout exceeds Polly's timeout
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

builder.AddAzureBlobClient("eshopsupport-blobs");
builder.AddChatCompletionService("chatcompletion");
builder.AddRedisClient("redis");

JsonWebTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["IdentityUrl"];
    options.TokenValidationParameters.ValidateAudience = false;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CustomerApi", policy => policy.RequireAuthenticatedUser())
    .AddFallbackPolicy("StaffApi", policy => policy.RequireRole("staff"));

var app = builder.Build();

var initialImportDataDir = builder.Configuration["ImportInitialDataDir"];
await AppDbContext.EnsureDbCreatedAsync(app.Services, initialImportDataDir);
await ProductSemanticSearch.EnsureSeedDataImportedAsync(app.Services, initialImportDataDir);
await ProductManualSemanticSearch.EnsureSeedDataImportedAsync(app.Services, initialImportDataDir);

app.MapAssistantApiEndpoints();
app.MapTicketApiEndpoints();
app.MapTicketMessagingApiEndpoints();
app.MapCatalogApiEndpoints();

app.Run();

// Polly Configuration for Retry, Timeout, and Circuit Breaker
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(2),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                Console.WriteLine($"Retry {retryCount} due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}. Waiting {timespan} before next retry.");
            });
}

static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
{
    return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(60));  // Increased timeout to 20 seconds
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromMinutes(2),  // Adjusted to break after 5 consecutive errors
            onBreak: (response, timespan) =>
            {
                Console.WriteLine($"Circuit broken for {timespan.TotalSeconds} seconds.");
            },
            onReset: () => Console.WriteLine("Circuit reset."),
            onHalfOpen: () => Console.WriteLine("Circuit is half-open, testing next request."));
}
