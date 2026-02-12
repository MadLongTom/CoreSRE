using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// Singleton-safe DbContext factory that creates its own DbContextOptions per call.
/// Used by AgentSessionStore (singleton) to avoid the scoped-options lifetime conflict
/// that occurs when using EF Core's built-in AddDbContextFactory alongside AddDbContext.
/// </summary>
internal sealed class StandaloneDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly string _connectionString;

    public StandaloneDbContextFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new AppDbContext(options);
    }
}
