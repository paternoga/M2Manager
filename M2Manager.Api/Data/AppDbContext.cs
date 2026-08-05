using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace M2Manager.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomOpening> RoomOpenings => Set<RoomOpening>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<ShoppingCategory> ShoppingCategories => Set<ShoppingCategory>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---------- Property ----------
        b.Entity<Property>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.Address).HasMaxLength(400);
            e.Property(p => p.TotalAreaM2).HasColumnType("numeric(10,2)");
            e.Property(p => p.DefaultRoomHeightM).HasColumnType("numeric(5,2)").HasDefaultValue(2.60m);
            e.HasIndex(p => p.Name);
        });

        // ---------- Room ----------
        b.Entity<Room>(e =>
        {
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.Property(r => r.Notes).HasMaxLength(2000);
            e.Property(r => r.FloorAreaM2).HasColumnType("numeric(10,2)");
            e.Property(r => r.LengthM).HasColumnType("numeric(8,3)");
            e.Property(r => r.WidthM).HasColumnType("numeric(8,3)");
            e.Property(r => r.HeightM).HasColumnType("numeric(5,2)");
            e.Property(r => r.ManualWallAreaM2).HasColumnType("numeric(10,2)");
            e.Property(r => r.ExcludedWallAreaM2).HasColumnType("numeric(10,2)");
            e.Property(r => r.ManualCeilingAreaM2).HasColumnType("numeric(10,2)");
            e.Property(r => r.IncludeInTotals).HasDefaultValue(true);

            e.HasOne(r => r.Property)
                .WithMany(p => p.Rooms)
                .HasForeignKey(r => r.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(r => new { r.PropertyId, r.SortOrder });
        });

        // ---------- RoomOpening ----------
        b.Entity<RoomOpening>(e =>
        {
            e.Property(o => o.WidthCm).HasColumnType("numeric(8,1)");
            e.Property(o => o.HeightCm).HasColumnType("numeric(8,1)");
            e.Property(o => o.OffsetCm).HasColumnType("numeric(8,1)");
            e.Property(o => o.Count).HasDefaultValue(1);
            e.Property(o => o.SubtractFromWalls).HasDefaultValue(true);
            e.Property(o => o.Notes).HasMaxLength(500);

            e.HasOne(o => o.Room)
                .WithMany(r => r.Openings)
                .HasForeignKey(o => o.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- ExpenseCategory ----------
        b.Entity<ExpenseCategory>(e =>
        {
            e.Property(c => c.Name).IsRequired().HasMaxLength(150);
            e.HasIndex(c => c.Name).IsUnique();
        });

        // ---------- Invoice ----------
        b.Entity<Invoice>(e =>
        {
            e.Property(i => i.Vendor).HasMaxLength(300);
            e.Property(i => i.Amount).HasColumnType("numeric(12,2)");
            e.Property(i => i.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("PLN");
            e.Property(i => i.Description).HasMaxLength(2000);
            e.Property(i => i.ImageObjectKey).IsRequired().HasMaxLength(500);

            e.HasOne(i => i.Property)
                .WithMany(p => p.Invoices)
                .HasForeignKey(i => i.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Skasowanie pomieszczenia nie może kasować dowodu zakupu.
            e.HasOne(i => i.Room)
                .WithMany(r => r.Invoices)
                .HasForeignKey(i => i.RoomId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(i => i.ExpenseCategory)
                .WithMany(c => c.Invoices)
                .HasForeignKey(i => i.ExpenseCategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(i => new { i.PropertyId, i.IssueDate });
            e.HasIndex(i => i.ExpenseCategoryId);
        });

        // ---------- ShoppingCategory ----------
        b.Entity<ShoppingCategory>(e =>
        {
            e.Property(c => c.Name).IsRequired().HasMaxLength(150);
            e.HasIndex(c => c.Name).IsUnique();
        });

        // ---------- ShoppingItem ----------
        b.Entity<ShoppingItem>(e =>
        {
            e.Property(i => i.Name).IsRequired().HasMaxLength(300);
            e.Property(i => i.Description).HasMaxLength(1000);
            e.Property(i => i.CalculationNotes).HasMaxLength(4000);
            e.Property(i => i.Unit).HasMaxLength(20);
            e.Property(i => i.Vendor).HasMaxLength(300);
            e.Property(i => i.Link).HasMaxLength(1000);
            e.Property(i => i.AssignedTo).HasMaxLength(100);
            e.Property(i => i.Quantity).HasColumnType("numeric(12,3)");
            e.Property(i => i.UnitCost).HasColumnType("numeric(12,2)");
            e.Property(i => i.TotalCost).HasColumnType("numeric(12,2)");
            e.Property(i => i.PlannedBudget).HasColumnType("numeric(12,2)");
            e.Property(i => i.ActualCost).HasColumnType("numeric(12,2)");

            e.HasOne(i => i.Property)
                .WithMany(p => p.ShoppingItems)
                .HasForeignKey(i => i.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(i => i.Room)
                .WithMany(r => r.ShoppingItems)
                .HasForeignKey(i => i.RoomId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(i => i.ShoppingCategory)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.ShoppingCategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(i => i.Invoice)
                .WithMany(inv => inv.ShoppingItems)
                .HasForeignKey(i => i.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(i => new { i.PropertyId, i.OrdinalNo });
            e.HasIndex(i => i.Status);
        });

        ApplyUtcDateTimeConversion(b);
    }

    /// <summary>
    /// Npgsql wymaga, żeby do kolumn `timestamptz` trafiał DateTime z Kind = Utc.
    /// Wymuszamy to konwerterem, zamiast pilnować tego w każdym miejscu zapisu.
    /// </summary>
    private static void ApplyUtcDateTimeConversion(ModelBuilder b)
    {
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableUtcConverter);
                }
            }
        }
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Automatyczne CreatedAt/UpdatedAt dla encji, które je mają.</summary>
    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            StampIfPresent(entry, now);
        }
    }

    private static void StampIfPresent(EntityEntry entry, DateTime now)
    {
        var createdAt = entry.Metadata.FindProperty(nameof(Invoice.CreatedAt));
        var updatedAt = entry.Metadata.FindProperty(nameof(Invoice.UpdatedAt));

        if (updatedAt is not null)
        {
            entry.Property(nameof(Invoice.UpdatedAt)).CurrentValue = now;
        }

        if (createdAt is not null && entry.State == EntityState.Added)
        {
            var current = entry.Property(nameof(Invoice.CreatedAt)).CurrentValue;
            if (current is null or DateTime { Year: <= 1 })
            {
                entry.Property(nameof(Invoice.CreatedAt)).CurrentValue = now;
            }
        }
    }
}
