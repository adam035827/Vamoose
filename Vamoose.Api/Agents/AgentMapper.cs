using Vamoose.Api.Domain;

namespace Vamoose.Api.Agents;

public static class AgentMapper
{
    private static readonly HashSet<string> EnergyLevels = ["Low", "Medium", "High"];

    public static AgentTripContext ToContext(Trip trip) => new(
        trip.Name,
        trip.Destination,
        trip.StartDate,
        trip.EndDate,
        trip.Notes,
        Math.Max(1, trip.Members.Count(m => m.Status == MemberStatus.Active)),
        trip.Status.ToString());

    public static AgentTripPlan ToPlan(Trip trip) => new([.. trip.Items.Select(i => new AgentPlanSlot(
        i.Key,
        i.Category.ToString(),
        i.Title,
        i.Day,
        i.Date,
        Math.Max(0, i.Options.FindIndex(o => o.Id == i.SelectedOptionId)),
        [.. i.Options.Select(o => new AgentPlanOption(
            o.Title,
            o.Description,
            o.Provider,
            o.Location,
            o.EstimatedCost is { } cost ? new AgentMoney(cost, o.Currency ?? "USD") : null,
            o.SourceUrl,
            o.PriceFetchedAt,
            o.StartTime,
            o.EndTime,
            o.EnergyLevel))]))]);

    // Builds plan items from agent output. Model output is validated here before it is persisted.
    public static List<PlanItem> ToItems(Guid tripId, AgentTripPlan plan)
    {
        var items = new List<PlanItem>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, index) in plan.Slots.Select((s, i) => (s, i)))
        {
            if (slot.Options.Count == 0 || !Enum.TryParse<PlanCategory>(slot.Category, out var category))
            {
                continue;
            }

            var key = keys.Add(slot.Key) ? slot.Key : $"{slot.Key}-{index}";
            keys.Add(key);
            var options = slot.Options.Take(4).Select((o, i) => new PlanOption
            {
                Id = Guid.NewGuid(),
                Title = Clip(o.Title, 200) ?? "Option",
                Description = Clip(o.Description, 600),
                Provider = Clip(o.Provider, 120),
                Location = Clip(o.LocationName, 200),
                EstimatedCost = o.EstimatedCostPerPerson?.Amount,
                Currency = o.EstimatedCostPerPerson?.Currency,
                SourceUrl = SafeUrl(o.SourceUrl),
                PriceFetchedAt = o.PriceFetchedAt,
                StartTime = Clip(o.StartTime, 5),
                EndTime = Clip(o.EndTime, 5),
                EnergyLevel = o.EnergyLevel is { } e && EnergyLevels.Contains(e) ? e : null,
                SortOrder = i,
            }).ToList();

            items.Add(new PlanItem
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                Key = Clip(key, 64) ?? $"slot-{index}",
                Category = category,
                Title = Clip(slot.Title, 120) ?? category.ToString(),
                Day = slot.Day,
                Date = slot.Date,
                SortOrder = index,
                SelectedOptionId = options[Math.Clamp(slot.SelectedIndex, 0, options.Count - 1)].Id,
                Options = options,
            });
        }
        return items;
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();

    // Only http(s) links are rendered, so a model can't smuggle javascript: URLs into the UI.
    private static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && url!.Length <= 1000
            ? url
            : null;
}
