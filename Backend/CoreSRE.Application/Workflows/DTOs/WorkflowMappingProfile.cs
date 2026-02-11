using AutoMapper;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// AutoMapper 映射配置：WorkflowDefinition / VOs ↔ DTOs
/// </summary>
public class WorkflowMappingProfile : Profile
{
    public WorkflowMappingProfile()
    {
        // WorkflowDefinition → WorkflowDefinitionDto
        CreateMap<WorkflowDefinition, WorkflowDefinitionDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));

        // WorkflowDefinition → WorkflowSummaryDto (list view with NodeCount)
        CreateMap<WorkflowDefinition, WorkflowSummaryDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.NodeCount, opt => opt.MapFrom(s => s.Graph.Nodes.Count));

        // WorkflowGraphVO → WorkflowGraphDto
        CreateMap<WorkflowGraphVO, WorkflowGraphDto>();

        // WorkflowNodeVO → WorkflowNodeDto
        CreateMap<WorkflowNodeVO, WorkflowNodeDto>()
            .ForMember(d => d.NodeType, opt => opt.MapFrom(s => s.NodeType.ToString()));

        // WorkflowEdgeVO → WorkflowEdgeDto
        CreateMap<WorkflowEdgeVO, WorkflowEdgeDto>()
            .ForMember(d => d.EdgeType, opt => opt.MapFrom(s => s.EdgeType.ToString()));

        // WorkflowExecution → WorkflowExecutionDto
        CreateMap<WorkflowExecution, WorkflowExecutionDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));

        // WorkflowExecution → WorkflowExecutionSummaryDto
        CreateMap<WorkflowExecution, WorkflowExecutionSummaryDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));

        // NodeExecutionVO → NodeExecutionDto
        CreateMap<NodeExecutionVO, NodeExecutionDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
    }
}
