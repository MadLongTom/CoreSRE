using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.PreviewContext;

public record PreviewContextCommand : IRequest<Result<ContextInitResultVO>>
{
    public List<ContextInitItemVO> Items { get; init; } = [];
    public Dictionary<string, string>? TemplateVariables { get; init; }
}
