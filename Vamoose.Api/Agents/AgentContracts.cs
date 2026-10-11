using System.Text.Json.Serialization;

namespace Vamoose.Api.Agents;

// Mirrors Vamoose.Agents/vamoose_agents/contracts.py. Serialized as camelCase.

public static class AgentActions
{
    public const string Chat = "Chat";
    public const string Research = "Research";
    public const string Questions = "Questions";
    public const string Analyze = "Analyze";
    public const string Nudge = "Nudge";
}

public static class NudgeKinds
{
    public const string Stay = "Stay";
}

public record AgentMoney(decimal Amount, string Currency);

public record AgentPlanOption(
    string Title,
    string? Description = null,
    string? Provider = null,
    string? LocationName = null,
    AgentMoney? EstimatedCostPerPerson = null,
    string? SourceUrl = null,
    DateTimeOffset? PriceFetchedAt = null,
    string? StartTime = null,
    string? EndTime = null,
    string? EnergyLevel = null);

public record AgentPlanSlot(
    string Key,
    string Category,
    string Title,
    int? Day,
    DateOnly? Date,
    int SelectedIndex,
    List<AgentPlanOption> Options);

public record AgentTripPlan(List<AgentPlanSlot> Slots);

public record AgentTripContext(
    string Name,
    string Destination,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Notes,
    int TravelerCount,
    string Status);

public record AgentHistoryMessage(string Role, string Text);

public record AgentNudgeContext(string Kind);

public record AgentParticipantChoice(string Participant, string Option);

public record AgentSlotResponses(string Key, string Title, string Selected, List<string> Alternatives, List<AgentParticipantChoice> Choices);

public record AgentResponses(List<string> Approved, List<string> Denied, List<string> Pending, List<AgentSlotResponses> Slots);

public record AgentInvocation(
    string RequestId,
    string Action,
    string? Message = null,
    AgentTripContext? Trip = null,
    AgentTripPlan? Plan = null,
    List<AgentHistoryMessage>? History = null,
    bool? CanEditPlan = null,
    AgentNudgeContext? Nudge = null,
    AgentResponses? Responses = null,
    // Routing only (one AgentCore session per trip); never sent to the agent.
    [property: JsonIgnore] Guid? TripId = null);

public record AgentQuestion(string SlotKey, string Type, string Prompt, List<string> Options);

// Decision is the option title the group goes with; for ties (TieBreak) the agent picked it, with Reason.
public record AgentItemAnalysis(
    string SlotKey,
    List<string> Approvers,
    List<string> Dissenters,
    List<string> Alternatives,
    string? Decision = null,
    bool TieBreak = false,
    string? Reason = null);

public record AgentAnalysis(bool WorksForAll, string Summary, List<AgentItemAnalysis>? Items);

public record AgentEvent(
    string RequestId,
    int Sequence,
    string Type,
    string? Step,
    string? Message,
    string? Reply,
    AgentTripPlan? Plan,
    string? Destination,
    string? TripName,
    List<AgentQuestion>? Questions,
    AgentAnalysis? Analysis);

public record AgentResult(
    string? Reply,
    AgentTripPlan? Plan,
    List<AgentQuestion>? Questions,
    AgentAnalysis? Analysis,
    string? Destination = null,
    string? TripName = null);
