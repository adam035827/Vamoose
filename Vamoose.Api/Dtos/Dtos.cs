using System.ComponentModel.DataAnnotations;

namespace Vamoose.Api.Dtos;

public record MagicLinkRequest([Required, MaxLength(64)] string Token);

public record UserDto(Guid Id, string Email);

public record MagicLinkResponse(UserDto User, Guid TripId);

public record StartIntroRequest([Required, MinLength(1), MaxLength(500)] string Message);

public record StartIntroResponse(string Reply, string Destination, string TripName);

public record StartTripRequest(
    [Required, MinLength(1), MaxLength(500)] string Prompt,
    [Required, MaxLength(200)] string Destination,
    [Required, MaxLength(120)] string TripName,
    [Required, EmailAddress, MaxLength(254)] string OrganizerEmail,
    [MaxLength(30)] List<string> TravelerEmails);

public record StartTripResponse(Guid TripId, UserDto User);

public record MagicLinkDto(string Email, string Role, string Link);

public record AddMemberRequest([Required, EmailAddress, MaxLength(254)] string Email);

public record ChatRequest([Required, MinLength(1), MaxLength(4000)] string Message);

public record SelectOptionRequest([Required] Guid OptionId);

public record ChoiceDto(Guid ItemId, Guid OptionId);

public record RespondRequest([Required, MinLength(1)] List<ChoiceDto> Choices);

public record TripSummaryDto(
    Guid Id,
    string Name,
    string Destination,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    string MyRole,
    string PrimaryEmail,
    int MemberCount);

public record MemberDto(Guid UserId, string Email, string Role, string Status, string? Approval);

public record OptionDto(
    Guid Id,
    string Title,
    string? Description,
    string? Provider,
    string? Location,
    decimal? EstimatedCost,
    string? Currency,
    string? SourceUrl,
    DateTimeOffset? PriceFetchedAt,
    string? StartTime,
    string? EndTime,
    string? EnergyLevel,
    int Votes);

public record PlanItemDto(
    Guid Id,
    string Key,
    string Category,
    string Title,
    int? Day,
    DateOnly? Date,
    Guid? SelectedOptionId,
    string? Question,
    Guid? MyChoiceOptionId,
    List<OptionDto> Options);

public record ChangeRequestDto(
    Guid Id,
    Guid ItemId,
    string ItemTitle,
    string RequestedByEmail,
    string FromTitle,
    string ToTitle,
    string Status,
    DateTimeOffset CreatedAt);

public record PermissionsDto(
    bool CanChat,
    bool CanEditPlan,
    bool CanAddMembers,
    bool CanSend,
    bool CanRespond,
    bool CanLock,
    bool CanUnlock,
    bool CanReopen,
    bool CanExit);

public record TripDetailDto(
    Guid Id,
    string Name,
    string Destination,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Notes,
    string Status,
    int PlanVersion,
    string MyRole,
    string PrimaryEmail,
    string? MyApproval,
    bool Consensus,
    DateTimeOffset? ResponseDeadline,
    List<MemberDto> Members,
    List<PlanItemDto> Items,
    List<ChangeRequestDto> ChangeRequests,
    PermissionsDto Permissions);

public record ChatMessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt);

public record ChatResponseDto(List<ChatMessageDto> Messages, bool PlanUpdated);

public record ExitPreviewDto(string Message, bool CancelsTrip);

public record NotificationDto(Guid Id, Guid? TripId, string Type, string Message, DateTimeOffset CreatedAt, bool Read);
