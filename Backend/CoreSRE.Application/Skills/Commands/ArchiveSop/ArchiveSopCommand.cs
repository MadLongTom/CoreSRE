using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.ArchiveSop;

/// <summary>归档 SOP</summary>
public record ArchiveSopCommand(Guid SkillId) : IRequest<Result<bool>>;
