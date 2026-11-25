using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Duo.Api.Data;

/// <summary>
/// Used by `dotnet ef` so migration scaffolding never boots the web host.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DuoDbContext>
{
    public DuoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Duo")
            ?? "Data Source=duo-design.db";

        var options = new DbContextOptionsBuilder<DuoDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new DuoDbContext(options);
    }
}
