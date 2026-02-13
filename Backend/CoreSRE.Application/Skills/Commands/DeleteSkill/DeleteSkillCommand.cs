using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.DeleteSkill;

/// <summary>删除技能（同时清理 S3 文件包）</summary>
public record DeleteSkillCommand(Guid Id) : IRequest<Result<bool>>;
