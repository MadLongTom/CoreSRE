using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// AgentRegistration 专用仓储实现
/// </summary>
public class AgentRegistrationRepository : Repository<AgentRegistration>, IAgentRegistrationRepository
{
    public AgentRegistrationRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<IEnumerable<AgentRegistration>> GetByTypeAsync(
        AgentType? type,
        CancellationToken cancellationToken = default)
    {
        if (type is null)
            return await _dbSet.ToListAsync(cancellationToken);

        return await _dbSet
            .Where(a => a.AgentType == type.Value)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgentRegistration>> SearchBySkillAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Use raw SQL with JSONB functions to find matching Agent IDs.
        // SqlQuery<T> uses FormattableString — {searchTerm} is auto-parameterized as @p0.
        var pattern = $"%{searchTerm}%";

        var matchingIds = await _context.Database
            .SqlQuery<Guid>($"""
                SELECT ar.id AS "Value"
                FROM agent_registrations ar
                WHERE ar.agent_type = 'A2A'
                  AND ar.agent_card IS NOT NULL
                  AND EXISTS (
                    SELECT 1
                    FROM jsonb_array_elements(ar.agent_card -> 'Skills') AS skill
                    WHERE skill ->> 'Name' ILIKE {pattern}
                       OR skill ->> 'Description' ILIKE {pattern}
                  )
                """)
            .ToListAsync(cancellationToken);

        if (matchingIds.Count == 0)
            return [];

        // Step 2: Load full entities via EF Core to ensure owned JSON properties
        // (AgentCard, Skills, etc.) are correctly materialized.
        var agents = await _dbSet
            .Where(a => matchingIds.Contains(a.Id))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return agents;
    }
}
