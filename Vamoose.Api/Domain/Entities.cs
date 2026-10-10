namespace Vamoose.Api.Domain;

public enum TripStatus { Planning, Agreement, Locked, Cancelled }

public enum MemberRole { Primary, Traveler }

public enum MemberStatus { Active, Exited }

// Same categories the agent's DraftItem uses (requirements 3.4).
public enum PlanCategory { Flight, Lodging, Activity, Dining, Transit, Free }

public enum ChatRole { User, Agent }

public enum ChangeRequestStatus { Pending, Approved, Denied, Superseded }

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Trip
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Destination { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; }
    public Guid PrimaryUserId { get; set; }
    public User PrimaryUser { get; set; } = null!;
    public TripStatus Status { get; set; } = TripStatus.Planning;
    public TripStatus? StatusBeforeCancel { get; set; }

    // Bumped on every plan change; approvals and nudge counts are tied to a version.
    public int PlanVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    // Travelers have 24 hours per plan version to respond and be considered (requirements 1).
    public DateTimeOffset? ResponseDeadline { get; set; }

    // Plan version whose response window has been closed and evaluated.
    public int? ClosedVersion { get; set; }
    public List<TripMember> Members { get; set; } = [];
    public List<PlanItem> Items { get; set; } = [];
}

public class TripMember
{
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public MemberRole Role { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Active;
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? ExitedAt { get; set; }

    // Secret in this member's magic link, the only access needed for the trip (requirements 3.2).
    public required string AccessToken { get; set; }
    public DateTimeOffset? LinkSentAt { get; set; }
}

public class PlanItem
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public required string Key { get; set; }
    public PlanCategory Category { get; set; }
    public required string Title { get; set; }
    public int? Day { get; set; }
    public DateOnly? Date { get; set; }
    public int SortOrder { get; set; }
    public Guid? SelectedOptionId { get; set; }

    // Agent-written preference question shown to travelers during agreement.
    public string? Question { get; set; }
    public List<PlanOption> Options { get; set; } = [];
}

public class PlanOption
{
    public Guid Id { get; set; }
    public Guid PlanItemId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Provider { get; set; }
    public string? Location { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? Currency { get; set; }
    public string? SourceUrl { get; set; }
    public DateTimeOffset? PriceFetchedAt { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? EnergyLevel { get; set; }
    public int SortOrder { get; set; }
}

public class ItemChoice
{
    public Guid PlanItemId { get; set; }
    public Guid UserId { get; set; }
    public Guid TripId { get; set; }
    public Guid OptionId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class PlanApproval
{
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public int PlanVersion { get; set; }
    public bool Approved { get; set; }
    public DateTimeOffset At { get; set; }
}

public class ChangeRequest
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid PlanItemId { get; set; }
    public PlanItem PlanItem { get; set; } = null!;
    public Guid RequestedByUserId { get; set; }
    public User RequestedBy { get; set; } = null!;
    public Guid FromOptionId { get; set; }
    public Guid ToOptionId { get; set; }
    public ChangeRequestStatus Status { get; set; } = ChangeRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }

    // Each member has a private thread with the agent for a trip.
    public Guid UserId { get; set; }
    public ChatRole Role { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Nudge
{
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanItemId { get; set; }
    public int PlanVersion { get; set; }
    public int Count { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TripId { get; set; }
    public required string Type { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
