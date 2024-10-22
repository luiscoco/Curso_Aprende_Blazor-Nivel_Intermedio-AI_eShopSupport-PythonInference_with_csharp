using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Extensions.Hosting
{
    public static class PreventStreamingWithFunctionsExtensions
    {
        public static ChatClientBuilder UsePreventStreamingWithFunctions(this ChatClientBuilder builder)
        {
            return builder.Use(inner => new PreventStreamingWithFunctions(inner));
        }

        private class PreventStreamingWithFunctions : DelegatingChatClient
        {
            public PreventStreamingWithFunctions(IChatClient innerClient) : base(innerClient) { }

            public override async Task<ChatCompletion> CompleteAsync(IList<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                Console.WriteLine("Received messages for completion: " + JsonSerializer.Serialize(chatMessages));

                // Check last message and reassign the role if it contains "$schema"
                if (chatMessages.Count > 1
                    && chatMessages.LastOrDefault() is { } lastMessage
                    && lastMessage.Role == ChatRole.System
                    && lastMessage.Text?.Contains("$schema", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Adjust the last message role to User if schema pattern is detected
                    lastMessage.Role = ChatRole.User;
                }

                try
                {
                    // Invoke the base CompleteAsync method
                    var result = await base.CompleteAsync(chatMessages, options, CancellationToken.None);
                    Console.WriteLine("Completion result: " + JsonSerializer.Serialize(result));
                    return result;
                }
                catch (Exception ex)
                {
                    // Handle and log errors during completion
                    Console.WriteLine($"Error during CompleteAsync: {ex.GetType()} - {ex.Message}");
                    throw;
                }
            }

            //public override async Task<ChatCompletion> CompleteAsync(IList<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            //{
            //    // Replace the provided chatMessages with a single test message
            //    var testMessages = new List<ChatMessage>
            //    {
            //        new ChatMessage
            //        {
            //            Role = ChatRole.User,
            //            Text = "hello"
            //        }
            //    };

            //    Console.WriteLine("Sending test message for completion: " + JsonSerializer.Serialize(testMessages));

            //    try
            //    {
            //        // Invoke the base CompleteAsync method with the test message
            //        var result = await base.CompleteAsync(testMessages, options, CancellationToken.None);
            //        Console.WriteLine("Completion result: " + JsonSerializer.Serialize(result));
            //        return result;
            //    }
            //    catch (Exception ex)
            //    {
            //        // Handle and log errors during completion
            //        Console.WriteLine($"Error during CompleteAsync: {ex.GetType()} - {ex.Message}");
            //        throw;
            //    }
            //}


            public override IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(IList<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                // Check if options contain any tools, if not, fallback to normal streaming
                return options?.Tools is null or []
                    ? base.CompleteStreamingAsync(chatMessages, options, cancellationToken)
                    : TreatNonstreamingAsStreaming(chatMessages, options, cancellationToken);
            }

            private async IAsyncEnumerable<StreamingChatCompletionUpdate> TreatNonstreamingAsStreaming(IList<ChatMessage> chatMessages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                Console.WriteLine("Handling non-streaming request as streaming.");

                // Call the CompleteAsync method to handle the request
                var result = await CompleteAsync(chatMessages, options, cancellationToken);

                // Yield results as a simulated stream
                for (var choiceIndex = 0; choiceIndex < result.Choices.Count; choiceIndex++)
                {
                    var choice = result.Choices[choiceIndex];
                    yield return new StreamingChatCompletionUpdate
                    {
                        AuthorName = choice.AuthorName,
                        ChoiceIndex = choiceIndex,
                        CompletionId = result.CompletionId,
                        Contents = choice.Contents,
                        CreatedAt = result.CreatedAt,
                        FinishReason = result.FinishReason,
                        RawRepresentation = choice.RawRepresentation,
                        Role = choice.Role,
                        AdditionalProperties = result.AdditionalProperties,
                    };
                }
            }
        }
    }
}
