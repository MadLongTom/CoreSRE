using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.SearchAgents;

/// <summary>
/// Agent 技能关键词搜索处理器
/// </summary>
public class SearchAgentsQueryHandler : IRequestHandler<SearchAgentsQuery, Result<AgentSearchResponse>>
{
    private readonly IAgentRegistrationRepository _repository;

    public SearchAgentsQueryHandler(IAgentRegistrationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<AgentSearchResponse>> Handle(
        SearchAgentsQuery request,
        CancellationToken cancellationToken)
    {
        var searchTerm = request.Query.Trim();

        // Step 1: Get matching agents from repository (JSONB ILIKE filtering)
        var agents = await _repository.SearchBySkillAsync(searchTerm, cancellationToken);

        // Step 2: For each agent, extract matched skills in C# (case-insensitive Contains)
        var results = agents
            .Select(agent =>
            {
                var matchedSkills = (agent.AgentCard?.Skills ?? [])
                    .Where(s =>
                        s.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        || (s.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(s => new MatchedSkillDto
                    {
                        Name = s.Name,
                        Description = s.Description
                    })
                    .ToList();

                return new AgentSearchResultDto
                {
                    Id = agent.Id,
                    Name = agent.Name,
                    AgentType = agent.AgentType.ToString(),
                    Status = agent.Status.ToString(),
                    CreatedAt = agent.CreatedAt,
                    MatchedSkills = matchedSkills,
                    SimilarityScore = null // P1: keyword mode — no similarity score
                };
            })
            // Step 3: Sort by matched skill count descending (FR-005)
            .OrderByDescending(r => r.MatchedSkills.Count)
            .ToList();

        // Step 4: Wrap in response envelope
        var response = new AgentSearchResponse
        {
            Results = results,
            SearchMode = "keyword",
            Query = searchTerm,
            TotalCount = results.Count
        };

        return Result<AgentSearchResponse>.Ok(response);
    }
}
