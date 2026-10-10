namespace Vamoose.Api.Agents;

// Mirrors Vamoose.Agents/vamoose_agents/contracts.py. Serialized as camelCase.

public static class AgentActions
{
    public const string Chat = "Chat";
    public const string Questions = "Questions";
    public const string Analyze = "Analyze";
    public const string Nudge = "Nudge";
    public const string Retain = "Retain";
}

public record AgentMoney(decimal Amount, string Currency);

public record AgentPlanOption(
    string Title,
    string? Description = null,
    string? Provider = null,
    string? LocationName = null,
    AgentMoney? Price = null,
    string? Url = null,
    string? StartTime = null,
    string? EndTime = null);

public record AgentPlanSlot(
    string Key,
    string Category,
    string Title,
    int? Day,
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

public record AgentNudgeContext(
    string SlotTitle,
    string UserChoice,
    string MajorityChoice,
    int UserChoiceCount,
    int MajorityCount,
    int TotalResponses,
    int NudgeNumber);

public record AgentOptionTally(string Option, int Count);

public record AgentSlotTally(string Key, string Title, string Selected, List<AgentOptionTally> Tallies);

public record AgentResponses(int Approved, int Denied, int Pending, List<AgentSlotTally> Slots);

public record AgentInvocation(
    string RequestId,
    string Action,
    string? Message = null,
    AgentTripContext? Trip = null,
    AgentTripPlan? Plan = null,
    List<AgentHistoryMessage>? History = null,
    bool? CanEditPlan = null,
    AgentNudgeContext? Nudge = null,
    AgentResponses? Responses = null);

public record AgentQuestion(string SlotKey, string Text);

public record AgentAnalysis(bool WorksForAll, string Summary);

public record AgentEvent(
    string RequestId,
    int Sequence,
    string Type,
    string? Step,
    string? Message,
    string? Reply,
    AgentTripPlan? Plan,
    List<AgentQuestion>? Questions,
    AgentAnalysis? Analysis);

public record AgentResult(string? Reply, AgentTripPlan? Plan, List<AgentQuestion>? Questions, AgentAnalysis? Analysis);
