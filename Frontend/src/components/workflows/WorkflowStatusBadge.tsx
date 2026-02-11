import { Badge } from "@/components/ui/badge";
import type { WorkflowStatus } from "@/types/workflow";

const statusConfig: Record<
  WorkflowStatus,
  { variant: "default" | "secondary" | "outline"; className?: string }
> = {
  Draft: { variant: "secondary" },
  Published: { variant: "default", className: "bg-green-600 hover:bg-green-700" },
};

interface WorkflowStatusBadgeProps {
  status: string;
}

export function WorkflowStatusBadge({ status }: WorkflowStatusBadgeProps) {
  const config =
    statusConfig[status as WorkflowStatus] ?? { variant: "outline" as const };
  return (
    <Badge variant={config.variant} className={config.className}>
      {status}
    </Badge>
  );
}
