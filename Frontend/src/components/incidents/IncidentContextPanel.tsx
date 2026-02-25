import type { IncidentDetail } from "@/types/incident";
import { ROUTE_LABELS } from "@/types/incident";
import { IncidentSeverityBadge } from "./IncidentSeverityBadge";
import { IncidentStatusBadge } from "./IncidentStatusBadge";
import { Separator } from "@/components/ui/separator";

export function IncidentContextPanel({
  incident,
}: {
  incident: IncidentDetail;
}) {
  return (
    <div className="space-y-4 p-4">
      {/* Header */}
      <div>
        <h3 className="text-sm font-semibold">事故详情</h3>
        <Separator className="mt-2" />
      </div>

      {/* Status + Severity */}
      <div className="flex flex-wrap gap-2">
        <IncidentSeverityBadge severity={incident.severity} />
        <IncidentStatusBadge status={incident.status} />
      </div>

      {/* Properties */}
      <dl className="space-y-3 text-sm">
        <InfoItem label="触发路由" value={ROUTE_LABELS[incident.route]} />
        <InfoItem label="告警名称" value={incident.alertName} />
        {incident.alertFingerprint && (
          <InfoItem label="指纹" value={incident.alertFingerprint} />
        )}
        {incident.conversationId && (
          <InfoItem label="对话 ID" value={incident.conversationId} />
        )}
        {incident.rootCause && (
          <div>
            <dt className="text-xs font-medium text-muted-foreground">
              根因分析
            </dt>
            <dd className="mt-1 whitespace-pre-wrap rounded-md bg-muted p-2 text-xs">
              {incident.rootCause}
            </dd>
          </div>
        )}
        {incident.resolution && (
          <div>
            <dt className="text-xs font-medium text-muted-foreground">
              处置结论
            </dt>
            <dd className="mt-1 whitespace-pre-wrap rounded-md bg-muted p-2 text-xs">
              {incident.resolution}
            </dd>
          </div>
        )}
        {incident.timeToDetect && (
          <InfoItem label="MTTD" value={incident.timeToDetect} />
        )}
        {incident.timeToResolve && (
          <InfoItem label="MTTR" value={incident.timeToResolve} />
        )}
      </dl>

      {/* Alert Labels */}
      {incident.alertLabels &&
        Object.keys(incident.alertLabels).length > 0 && (
          <div>
            <h4 className="text-xs font-medium text-muted-foreground">
              告警标签
            </h4>
            <div className="mt-1 flex flex-wrap gap-1">
              {Object.entries(incident.alertLabels).map(([k, v]) => (
                <span
                  key={k}
                  className="rounded-md bg-muted px-1.5 py-0.5 text-xs"
                >
                  {k}={v}
                </span>
              ))}
            </div>
          </div>
        )}
    </div>
  );
}

function InfoItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5">{value}</dd>
    </div>
  );
}
