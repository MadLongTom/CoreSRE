import { Badge } from "@/components/ui/badge";
import type { SandboxStatus } from "@/types/sandbox";

const statusConfig: Record<SandboxStatus, { label: string; variant: "default" | "secondary" | "destructive" | "outline" }> = {
  Creating: { label: "创建中", variant: "outline" },
  Running: { label: "运行中", variant: "default" },
  Stopped: { label: "已停止", variant: "secondary" },
  Terminated: { label: "已终止", variant: "destructive" },
};

export function SandboxStatusBadge({ status }: { status: SandboxStatus | string }) {
  const config = statusConfig[status as SandboxStatus] ?? { label: status, variant: "outline" as const };
  return <Badge variant={config.variant}>{config.label}</Badge>;
}
