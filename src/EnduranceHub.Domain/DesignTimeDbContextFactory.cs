using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnduranceHub.Domain;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EnduranceHubDbContext>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=endurancehub;Username=postgres;Password=postgres";

    public EnduranceHubDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EnduranceHubDbContext>()
            .UseNpgsql(DefaultConnectionString)
            .Options;
        return new EnduranceHubDbContext(options);
    }
}