using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.ComponentModel;

namespace eShopSupport.Backend.Api
{
    public static class AssistantApi
    {
        public static void MapAssistantApiEndpoints(this WebApplication app)
        {
            app.MapPost("/api/assistant/chat", GetStreamingChatResponseAsync);
        }

        private static async Task GetStreamingChatResponseAsync(AssistantChatRequest request, HttpContext httpContext, AppDbContext dbContext, IChatClient chatClient, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
        {
            var logger = loggerFactory.CreateLogger("AssistantApi");

            // Set response headers for chunked transfer and no caching
            httpContext.Response.Headers["Cache-Control"] = "no-cache";
            httpContext.Response.Headers["Transfer-Encoding"] = "chunked";
            httpContext.Response.ContentType = "application/json";

            // Initialize response stream with an empty array
            await httpContext.Response.WriteAsync("[", cancellationToken);

            try
            {
                var product = request.ProductId.HasValue
                    ? await dbContext.Products.FindAsync(request.ProductId.Value)
                    : null;

                // Build the prompt and messages
                var messages = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System, $$"""
                    You are a helpful AI assistant called 'Assistant' whose job is to help customer service agents working for AdventureWorks, an online retailer.
                    The customer service agent is currently handling the following ticket:

                    <product_id>{{request.ProductId}}</product_id>
                    <product_name>{{product?.Model ?? "None specified"}}</product_name>
                    <customer_name>{{request.CustomerName}}</customer_name>
                    <summary>{{request.TicketSummary}}</summary>

                    The most recent message from the customer is this:
                    <customer_message>{{request.TicketLastCustomerMessage}}</customer_message>
                    """)
                };

                messages.AddRange(request.Messages.Select(m => new ChatMessage(m.IsAssistant ? ChatRole.Assistant : ChatRole.User, m.Text)));

                var searchManual = AIFunctionFactory.Create(new SearchManualContext(httpContext).SearchManual);
                var executionSettings = new ChatOptions
                {
                    Temperature = 0,
                    Tools = new[] { searchManual },
                    AdditionalProperties = new() { ["seed"] = 0 }
                };

                // Set a longer timeout for chat operations
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(30)); // Adjust as needed

                var streamingAnswer = chatClient.CompleteStreamingAsync(messages, executionSettings, linkedCts.Token);

                var answerBuilder = new StringBuilder();
                await foreach (var chunk in streamingAnswer.WithCancellation(linkedCts.Token))
                {
                    var serializedChunk = JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.AnswerChunk, chunk.ToString()));
                    await httpContext.Response.WriteAsync(serializedChunk, cancellationToken);
                    await httpContext.Response.WriteAsync(",\n", cancellationToken);
                    answerBuilder.Append(chunk.ToString());

                    logger.LogInformation("Streaming chunk: {Chunk}", serializedChunk);
                }

                // Handle classification or final response logic if needed
            }
            catch (TaskCanceledException ex)
            {
                logger.LogError(ex, "The operation was canceled due to timeout or other cancellation.");
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error = "The operation was canceled due to timeout." }), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while streaming chat response.");
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error = "An error occurred while processing the request." }), cancellationToken);
            }
            finally
            {
                // Close the JSON array
                await httpContext.Response.WriteAsync("]", cancellationToken);
            }
        }

        private class MessageClassification
        {
            public bool IsAddressedToCustomerByName { get; set; }
        }

        private class SearchManualContext
        {
            private readonly HttpContext _httpContext;
            private readonly SemaphoreSlim _semaphore = new(1);
            private readonly ProductManualSemanticSearch _manualSearch;

            public SearchManualContext(HttpContext httpContext)
            {
                _httpContext = httpContext;
                _manualSearch = _httpContext.RequestServices.GetRequiredService<ProductManualSemanticSearch>();
            }

            public async Task<object> SearchManual(
                [Description("A phrase to use when searching the manual")] string searchPhrase,
                [Description("ID for the product whose manual to search. Set to null only if you must search across all product manuals.")] int? productId)
            {
                await _semaphore.WaitAsync();

                try
                {
                    await _httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.Search, searchPhrase)));
                    await _httpContext.Response.WriteAsync(",\n");

                    var searchResults = await _manualSearch.SearchAsync(productId, searchPhrase);
                    foreach (var r in searchResults)
                    {
                        await _httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(
                            AssistantChatReplyItemType.SearchResult,
                            string.Empty,
                            int.Parse(r.Metadata.Id),
                            GetProductId(r),
                            GetPageNumber(r))));
                        await _httpContext.Response.WriteAsync(",\n");
                    }

                    return searchResults.Select(r => new
                    {
                        ProductId = GetProductId(r),
                        SearchResultId = r.Metadata.Id,
                        r.Metadata.Text,
                    });
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        private static int? GetProductId(MemoryQueryResult result)
        {
            var match = Regex.Match(result.Metadata.ExternalSourceName, @"productid:(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }

        private static int? GetPageNumber(MemoryQueryResult result)
        {
            var match = Regex.Match(result.Metadata.AdditionalMetadata, @"pagenumber:(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
    }
}
