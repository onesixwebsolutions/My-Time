using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DayGrid.Infrastructure.Data;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` construct an AppDbContext at
/// design time, without needing DayGrid.Api's full Program.cs bootstrap. Reads the connection
/// string the same way Program.cs does (DAYGRID_CONNECTION env var, or config), falling back to
/// the plan's documented local default.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString = "Host=localhost;Database=daygrid;Username=daygrid;Password=dev";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DAYGRID_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? FallbackConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
