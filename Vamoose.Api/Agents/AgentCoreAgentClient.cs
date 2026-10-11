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

    public async Task<AgentResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct)
    {
        var request = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = config["Agents:RuntimeArn"],
            RuntimeSessionId = $"vamoose-{Guid.NewGuid():N}",
            ContentType = "application/json",
            Payload = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(invocation, Json)),
        };

        InvokeAgentRuntimeResponse response;
        try
        {
            response = await agentCore.InvokeAgentRuntimeAsync(request, ct);
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "AgentCore call failed for {Action}", invocation.Action);
            throw new AgentUnavailableException("The Vamoose agent is not reachable right now. Please try again.");
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
}
