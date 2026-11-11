using EnduranceHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnduranceHub.Domain;

public class EnduranceHubDbContext : DbContext
{
    public EnduranceHubDbContext(DbContextOptions<EnduranceHubDbContext> options) : base(options) { }

    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Stint> Stints => Set<Stint>();
    public DbSet<Lap> Laps => Set<Lap>();
    public DbSet<Setup> Setups => Set<Setup>();
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(e =>
        {
            e.HasIndex(d => new { d.FirstName, d.LastName }).HasDatabaseName("IX_Driver_FullName");
        });

        modelBuilder.Entity<Car>(e =>
        {
            e.HasIndex(c => c.CarModelType).HasDatabaseName("IX_Car_CarModelType");
            e.HasIndex(c => c.RaceNumber).HasDatabaseName("IX_Car_RaceNumber");
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.HasIndex(ev => new { ev.EventIndex, ev.SessionIndex }).HasDatabaseName("IX_Event_EventSession");
            e.HasOne(ev => ev.Car)
                .WithMany()
                .HasForeignKey(ev => ev.CarId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Stint>(e =>
        {
            e.HasOne(s => s.Event)
                .WithMany(ev => ev.Stints)
                .HasForeignKey(s => s.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Driver)
                .WithMany(d => d.Stints)
                .HasForeignKey(s => s.DriverId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Car)
                .WithMany(c => c.Stints)
                .HasForeignKey(s => s.CarId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Lap>(e =>
        {
            e.HasOne(l => l.Event)
                .WithMany(ev => ev.Laps)
                .HasForeignKey(l => l.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Car)
                .WithMany(c => c.Laps)
                .HasForeignKey(l => l.CarId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.Driver)
                .WithMany(d => d.Laps)
                .HasForeignKey(l => l.DriverId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.Stint)
                .WithMany(s => s.Laps)
                .HasForeignKey(l => l.StintId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(l => new { l.EventId, l.LapNumber, l.DriverId }).HasDatabaseName("IX_Lap_EventLapDriver");
        });

        modelBuilder.Entity<Setup>(e =>
        {
            e.HasOne(s => s.Car)
                .WithMany(c => c.Setups)
                .HasForeignKey(s => s.CarId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.CarModelType, s.TrackName }).HasDatabaseName("IX_Setup_CarTrack");
        });

        modelBuilder.Entity<TelemetrySample>(e =>
        {
            e.HasOne(t => t.Driver)
                .WithMany(d => d.TelemetrySamples)
                .HasForeignKey(t => t.DriverId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Stint)
                .WithMany(s => s.TelemetrySamples)
                .HasForeignKey(t => t.StintId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.DriverId, t.Timestamp }).HasDatabaseName("IX_Telemetry_DriverTime");
            e.HasIndex(t => t.StintId).HasDatabaseName("IX_Telemetry_Stint");
        });
    }
}