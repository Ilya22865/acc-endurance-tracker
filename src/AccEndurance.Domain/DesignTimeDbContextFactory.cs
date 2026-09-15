using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AccEndurance.Domain;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccEnduranceDbContext>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=acc_endurance;Username=postgres;Password=postgres";

    public AccEnduranceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccEnduranceDbContext>()
            .UseNpgsql(DefaultConnectionString)
            .Options;
        return new AccEnduranceDbContext(options);
    }
}