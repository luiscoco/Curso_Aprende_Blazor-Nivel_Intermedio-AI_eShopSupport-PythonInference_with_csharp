using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

namespace eShopSupport.Backend.Api
{
    public static class PollyConfiguration
    {
        public static IServiceCollection AddHttpClientWithPolly(this IServiceCollection services)
        {
            // Standard HttpClient with Polly policies
            services.AddHttpClient("MyHttpClient")
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetTimeoutPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            // Additional HttpClient for long-running requests (if needed)
            services.AddHttpClient("LongRunningHttpClient")
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30)))
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.RequestTimeout) // Retry on timeouts
                .WaitAndRetryAsync(
                    retryCount: 3, // Retry 3 times
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(3), // Fixed 3-second delay between retries
                    onRetry: (response, timespan, retryAttempt, context) =>
                    {
                        Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds} seconds due to: {response.Exception?.Message ?? response.Result.StatusCode.ToString()}");
                    });
        }

        private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
        {
            // Timeout for standard requests
            return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(60)); // Timeout set to 10 seconds
        }

        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 3, // Circuit breaker trips after 3 consecutive failures
                    durationOfBreak: TimeSpan.FromMinutes(1), // Stays open for 1 minute
                    onBreak: (response, timespan) =>
                    {
                        Console.WriteLine($"Circuit broken due to: {response.Exception?.Message ?? response.Result.StatusCode.ToString()}. Breaking for {timespan.TotalSeconds} seconds.");
                    },
                    onReset: () => Console.WriteLine("Circuit reset."),
                    onHalfOpen: () => Console.WriteLine("Circuit is half-open, testing next request."));
        }
    }
}
