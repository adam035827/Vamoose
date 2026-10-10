using System.Net.Http.Json;
using System.Text.Json;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Agents;

public interface IAgentClient
{
    Task<AgentResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct);
}

public static class AgentRequests
{
    public static string NewId() => $"req-{Guid.NewGuid():N}";
}

// Calls the Strands agent service over its AgentCore /invocations contract and reads the SSE stream.
public class HttpAgentClient(HttpClient http, ILogger<HttpAgentClient> logger) : IAgentClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<AgentResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("invocations", invocation, Json, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Agent service call failed for {Action}", invocation.Action);
            throw new AgentUnavailableException("The Vamoose agent is not reachable right now. Please try again.");
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
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
                        return new AgentResult(evt.Reply, evt.Plan, evt.Questions, evt.Analysis);
                    case "Error":
                        throw new AgentUnavailableException(evt.Message ?? "The Vamoose agent hit a problem. Please try again.");
                }
            }
        }

        throw new AgentUnavailableException("The Vamoose agent did not return a result. Please try again.");
    }
}
