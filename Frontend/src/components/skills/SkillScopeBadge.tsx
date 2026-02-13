import { Badge } from "@/components/ui/badge";
import type { SkillScope } from "@/types/skill";

const scopeConfig: Record<SkillScope, { label: string; variant: "default" | "secondary" | "outline" }> = {
  Builtin: { label: "内置", variant: "default" },
  User: { label: "用户", variant: "secondary" },
  Project: { label: "项目", variant: "outline" },
};

export function SkillScopeBadge({ scope }: { scope: SkillScope | string }) {
  const config = scopeConfig[scope as SkillScope] ?? { label: scope, variant: "outline" as const };
  return <Badge variant={config.variant}>{config.label}</Badge>;
}
