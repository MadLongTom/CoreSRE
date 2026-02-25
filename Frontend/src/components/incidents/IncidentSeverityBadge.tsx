import { Badge } from "@/components/ui/badge";
import type { IncidentSeverity } from "@/types/incident";
import { cn } from "@/lib/utils";

const SEVERITY_STYLES: Record<IncidentSeverity, string> = {
  P1: "bg-red-600 text-white",
  P2: "bg-orange-500 text-white",
  P3: "bg-yellow-500 text-black",
  P4: "bg-blue-500 text-white",
};

export function IncidentSeverityBadge({
  severity,
  className,
}: {
  severity: IncidentSeverity;
  className?: string;
}) {
  return (
    <Badge className={cn(SEVERITY_STYLES[severity], className)}>
      {severity}
    </Badge>
  );
}
