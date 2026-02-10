import { Badge } from "@/components/ui/badge";
import type { AgentStatus } from "@/types/agent";

const statusVariant: Record<
  AgentStatus,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Registered: "outline",
  Active: "default",
  Inactive: "secondary",
  Error: "destructive",
};

interface AgentStatusBadgeProps {
  status: string;
}

export function AgentStatusBadge({ status }: AgentStatusBadgeProps) {
  const variant =
    statusVariant[status as AgentStatus] ?? "outline";
  return <Badge variant={variant}>{status}</Badge>;
}
