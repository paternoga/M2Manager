using M2Manager.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace M2Manager.Api.Data;

/// <summary>
/// Używane wyłącznie przez narzędzia EF (`dotnet ef migrations add`).
/// Dzięki temu generowanie migracji nie wymaga działającej bazy ani kompletu sekretów.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = DatabaseConnection.Resolve(configuration)
                               ?? "Host=localhost;Port=5432;Database=m2manager;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
