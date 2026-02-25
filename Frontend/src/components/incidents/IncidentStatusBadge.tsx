import { Badge } from "@/components/ui/badge";
import type { IncidentStatus } from "@/types/incident";
import { STATUS_LABELS } from "@/types/incident";
import { cn } from "@/lib/utils";

const STATUS_STYLES: Record<IncidentStatus, string> = {
  Open: "bg-gray-500 text-white",
  Investigating: "bg-blue-600 text-white",
  Mitigated: "bg-yellow-600 text-white",
  Resolved: "bg-green-600 text-white",
  Closed: "bg-gray-400 text-white",
  Escalated: "bg-red-500 text-white",
};

export function IncidentStatusBadge({
  status,
  className,
}: {
  status: IncidentStatus;
  className?: string;
}) {
  return (
    <Badge className={cn(STATUS_STYLES[status], className)}>
      {STATUS_LABELS[status]}
    </Badge>
  );
}
