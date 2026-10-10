using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Vamoose.Api.Domain;

namespace Vamoose.Api.Data;

public class VamooseDbContext(DbContextOptions<VamooseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripMember> TripMembers => Set<TripMember>();
    public DbSet<PlanItem> PlanItems => Set<PlanItem>();
    public DbSet<PlanOption> PlanOptions => Set<PlanOption>();
    public DbSet<ItemChoice> ItemChoices => Set<ItemChoice>();
    public DbSet<PlanApproval> PlanApprovals => Set<PlanApproval>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Nudge> Nudges => Set<Nudge>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Trip>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Destination).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.StatusBeforeCancel).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.PrimaryUser).WithMany().HasForeignKey(x => x.PrimaryUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TripMember>(e =>
        {
            e.HasKey(x => new { x.TripId, x.UserId });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AccessToken).HasMaxLength(64);
            e.HasIndex(x => x.AccessToken).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PlanItem>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Question).HasMaxLength(400);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.PlanItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlanOption>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(600);
            e.Property(x => x.Provider).HasMaxLength(120);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.EstimatedCost).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.SourceUrl).HasMaxLength(1000);
            e.Property(x => x.EnergyLevel).HasMaxLength(10);
            e.Property(x => x.StartTime).HasMaxLength(5);
            e.Property(x => x.EndTime).HasMaxLength(5);
        });

        // Choices and change requests go away with their plan item when the agent replaces the plan.
        b.Entity<ItemChoice>(e =>
        {
            e.HasKey(x => new { x.PlanItemId, x.UserId });
            e.HasOne<PlanItem>().WithMany().HasForeignKey(x => x.PlanItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TripId);
        });

        b.Entity<PlanApproval>(e => e.HasKey(x => new { x.TripId, x.UserId }));

        b.Entity<ChangeRequest>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.PlanItem).WithMany().HasForeignKey(x => x.PlanItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TripId);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Content).HasMaxLength(8000);
            e.HasIndex(x => new { x.TripId, x.UserId, x.CreatedAt });
        });

        b.Entity<Nudge>(e =>
        {
            e.HasKey(x => new { x.TripId, x.UserId, x.PlanItemId });
            e.HasOne<PlanItem>().WithMany().HasForeignKey(x => x.PlanItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(40);
            e.Property(x => x.Message).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        // SQLite (used by the API tests) can't order or compare DateTimeOffset, so store it as a number there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var property in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
