using System.Collections.Concurrent;
using Vamoose.Api.Agents;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Tests;

// Deterministic stand-in for the agent service.
public class FakeAgentClient : IAgentClient
{
    public ConcurrentQueue<AgentInvocation> Calls { get; } = new();

    public bool Down { get; set; }

    public Task<AgentResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct)
    {
        Calls.Enqueue(invocation);
        if (Down)
        {
            throw new AgentUnavailableException("The Vamoose agent is not reachable right now. Please try again.");
        }

        return Task.FromResult(invocation.Action switch
        {
            AgentActions.Chat when invocation.Trip is null =>
                new AgentResult("Lisbon is lovely.", null, null, null, "Lisbon, Portugal", "Lisbon trip"),
            AgentActions.Chat when invocation.CanEditPlan == true && invocation.Plan is null && invocation.History is { Count: > 0 } =>
                new AgentResult("Here's a plan.", Plan(), null, null),
            AgentActions.Chat => new AgentResult(invocation.Nudge is null ? "Who's coming and when?" : "Most of the group chose the apartment.", null, null, null),
            AgentActions.Research => new AgentResult("I found real options.", new AgentTripPlan(
                [.. invocation.Plan!.Slots.Select(s => s with { Options = [.. s.Options, new AgentPlanOption("Researched pick", SourceUrl: "https://example.com/real")] })]), null, null),
            AgentActions.Questions => new AgentResult(null, null,
                [.. invocation.Plan!.Slots.Select(s => new AgentQuestion(s.Key, "SingleChoice", $"OK with {s.Title}?", [.. s.Options.Select(o => o.Title)]))], null),
            AgentActions.Analyze => new AgentResult(null, null, null, new AgentAnalysis(
                false, "Summary.", [.. invocation.Responses!.Slots.Select(Decide)])),
            AgentActions.Nudge => new AgentResult("Please stay!", null, null, null),
            _ => throw new AgentUnavailableException("Unknown action."),
        });
    }

    // Majority wins; ties go to the last tied option so tests can tell an agent pick from the current one.
    private static AgentItemAnalysis Decide(AgentSlotResponses slot)
    {
        var votes = slot.Choices.GroupBy(c => c.Option).ToDictionary(g => g.Key, g => g.Count());
        var top = votes.Values.DefaultIfEmpty(0).Max();
        var leaders = new[] { slot.Selected }.Concat(slot.Alternatives).Where(o => votes.GetValueOrDefault(o) == top).ToList();
        var decision = leaders.Count > 1 ? leaders[^1] : leaders.FirstOrDefault() ?? slot.Selected;
        return new AgentItemAnalysis(
            slot.Key,
            [.. slot.Choices.Where(c => c.Option == decision).Select(c => c.Participant)],
            [.. slot.Choices.Where(c => c.Option != decision).Select(c => c.Participant)],
            [],
            decision,
            leaders.Count > 1,
            leaders.Count > 1 ? "Better value." : null);
    }

    public static AgentTripPlan Plan()
    {
        var fetched = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        AgentPlanOption Option(string title, decimal cost) =>
            new(title, EstimatedCostPerPerson: new AgentMoney(cost, "USD"), SourceUrl: "https://example.com", PriceFetchedAt: fetched);
        return new AgentTripPlan(
        [
            new AgentPlanSlot("lodging-main", "Lodging", "Where we stay", 1, null, 0, [Option("Apartment", 360), Option("Hostel", 190)]),
            new AgentPlanSlot("dinner-day-1", "Dining", "Welcome dinner", 1, null, 0, [Option("Market", 35), Option("Seafood", 70)]),
        ]);
    }
}
