import { Badge } from "@/components/ui/badge";
import type { SkillStatus } from "@/types/skill";
import { SKILL_STATUS_LABELS } from "@/types/skill";

const statusVariant: Record<SkillStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Active: "default",
  Inactive: "secondary",
  Draft: "outline",
  Reviewed: "default",
  Rejected: "destructive",
  Archived: "secondary",
  Superseded: "secondary",
  Invalid: "destructive",
  Degraded: "destructive",
};

const statusColor: Partial<Record<SkillStatus, string>> = {
  Active: "bg-green-100 text-green-800 border-green-200",
  Reviewed: "bg-blue-100 text-blue-800 border-blue-200",
  Draft: "bg-yellow-100 text-yellow-800 border-yellow-200",
  Degraded: "bg-orange-100 text-orange-800 border-orange-200",
};

export function SkillStatusBadge({ status }: { status: SkillStatus | string }) {
  const label = SKILL_STATUS_LABELS[status as SkillStatus] ?? status;
  const variant = statusVariant[status as SkillStatus] ?? "outline";
  const color = statusColor[status as SkillStatus];

  return (
    <Badge variant={variant} className={color}>
      {label}
    </Badge>
  );
}
