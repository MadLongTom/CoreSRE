import { Link } from "react-router";
import type { IncidentSummary } from "@/types/incident";
import { ROUTE_LABELS } from "@/types/incident";
import { IncidentSeverityBadge } from "./IncidentSeverityBadge";
import { IncidentStatusBadge } from "./IncidentStatusBadge";
import { Clock } from "lucide-react";

export function IncidentCard({ incident }: { incident: IncidentSummary }) {
  const createdAt = new Date(incident.createdAt);
  const timeAgo = getRelativeTime(createdAt);

  return (
    <Link
      to={`/incidents/${incident.id}`}
      className="block rounded-lg border bg-card p-4 transition-colors hover:bg-accent/50"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <IncidentSeverityBadge severity={incident.severity} />
            <IncidentStatusBadge status={incident.status} />
            <span className="text-xs text-muted-foreground">
              {ROUTE_LABELS[incident.route]}
            </span>
          </div>
          <h3 className="mt-2 truncate text-sm font-medium">
            {incident.title}
          </h3>
          <p className="mt-1 text-xs text-muted-foreground">
            {incident.alertName}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-1 text-xs text-muted-foreground">
          <Clock className="h-3 w-3" />
          <span>{timeAgo}</span>
        </div>
      </div>
    </Link>
  );
}

function getRelativeTime(date: Date): string {
  const now = Date.now();
  const diffMs = now - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);

  if (diffMins < 1) return "刚刚";
  if (diffMins < 60) return `${diffMins} 分钟前`;

  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours} 小时前`;

  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays} 天前`;
}
