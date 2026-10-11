using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Amazon.Runtime;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Agents;

// Calls the deployed AgentCore runtime signed with the host's AWS credentials (the EC2 instance role), and reads the same event stream as HttpAgentClient.
public class AgentCoreAgentClient(IAmazonBedrockAgentCore agentCore, IConfiguration config, ILogger<AgentCoreAgentClient> logger) : IAgentClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // Calls without a trip (the intro) share one session per API process.
    private static readonly string SharedSessionId = $"vamoose-api-{Guid.NewGuid():N}";
    private const int MaxAttempts = 3;

    public async Task<AgentResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(invocation, Json);
        var sessionId = SessionId(invocation);

        InvokeAgentRuntimeResponse response;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                response = await agentCore.InvokeAgentRuntimeAsync(new InvokeAgentRuntimeRequest
                {
                    AgentRuntimeArn = config["Agents:RuntimeArn"],
                    RuntimeSessionId = sessionId,
                    ContentType = "application/json",
                    Payload = new MemoryStream(payload),
                }, ct);
                break;
            }
            catch (RetryableConflictException) when (attempt < MaxAttempts)
            {
                // Brief 409 while AgentCore starts the session's microVM.
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
            catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "AgentCore call failed for {Action}", invocation.Action);
                throw new AgentUnavailableException("The Vamoose agent is not reachable right now. Please try again.");
            }
        }

        using (response)
        {
            using var reader = new StreamReader(response.Response);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                var evt = JsonSerializer.Deserialize<AgentEvent>(line["data: ".Length..], Json);
                switch (evt?.Type)
                {
                    case "Result":
                        return new AgentResult(evt.Reply, evt.Plan, evt.Questions, evt.Analysis, evt.Destination, evt.TripName);
                    case "Error":
                        throw new AgentUnavailableException(evt.Message ?? "The Vamoose agent hit a problem. Please try again.");
                }
            }
        }

        throw new AgentUnavailableException("The Vamoose agent did not return a result. Please try again.");
    }

    // A new session id per call meant a cold microVM every time; one per trip stays warm between calls.
    // Background research gets its own session so it doesn't compete with the organizer's chat.
    public static string SessionId(AgentInvocation invocation) => invocation.TripId switch
    {
        { } tripId when invocation.Action == AgentActions.Research => $"vamoose-research-{tripId:N}",
        { } tripId => $"vamoose-trip-{tripId:N}",
        null => SharedSessionId,
    };
}
