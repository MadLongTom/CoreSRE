import { Badge } from "@/components/ui/badge";
import type { ExecutionStatus, NodeExecutionStatus } from "@/types/workflow";

const statusConfig: Record<
  ExecutionStatus | NodeExecutionStatus,
  { variant: "default" | "secondary" | "destructive" | "outline"; className?: string }
> = {
  Pending: { variant: "secondary" },
  Running: { variant: "default", className: "bg-blue-600 hover:bg-blue-700" },
  Completed: { variant: "default", className: "bg-green-600 hover:bg-green-700" },
  Failed: { variant: "destructive" },
  Canceled: { variant: "outline", className: "border-yellow-500 text-yellow-700" },
  Skipped: { variant: "secondary", className: "opacity-60" },
};

interface ExecutionStatusBadgeProps {
  status: string;
}

export function ExecutionStatusBadge({ status }: ExecutionStatusBadgeProps) {
  const config =
    statusConfig[status as ExecutionStatus] ?? { variant: "outline" as const };
  return (
    <Badge variant={config.variant} className={config.className}>
      {status}
    </Badge>
  );
}
