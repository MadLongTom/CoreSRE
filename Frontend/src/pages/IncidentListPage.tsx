import { useCallback, useEffect, useState } from "react";
import { PageHeader } from "@/components/layout/PageHeader";
import { IncidentCard } from "@/components/incidents/IncidentCard";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Loader2, RefreshCw, ShieldAlert } from "lucide-react";
import { useIncidentSignalR } from "@/hooks/use-incident-signalr";
import type { IncidentSummary, IncidentStatus, IncidentSeverity } from "@/types/incident";
import type { ApiResult } from "@/types/agent";
import { INCIDENT_STATUSES, INCIDENT_SEVERITIES } from "@/types/incident";

const API_BASE = "/api/incidents";

export default function IncidentListPage() {
  const [incidents, setIncidents] = useState<IncidentSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [severityFilter, setSeverityFilter] = useState<string>("all");

  const fetchIncidents = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (statusFilter !== "all") params.set("status", statusFilter);
      if (severityFilter !== "all") params.set("severity", severityFilter);

      const resp = await fetch(`${API_BASE}?${params}`);
      const result: ApiResult<IncidentSummary[]> = await resp.json();
      if (result.success) {
        setIncidents(result.data ?? []);
      } else {
        setError(result.error ?? "Failed to load incidents");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Network error");
    } finally {
      setLoading(false);
    }
  }, [statusFilter, severityFilter]);

  useEffect(() => {
    fetchIncidents();
  }, [fetchIncidents]);

  // SignalR: real-time updates
  const { connectionState } = useIncidentSignalR("list", undefined, {
    onIncidentCreated: (evt) => {
      setIncidents((prev) => [
        {
          id: evt.incidentId,
          title: evt.title,
          status: evt.status as IncidentStatus,
          severity: evt.severity as IncidentSeverity,
          route: evt.route as IncidentSummary["route"],
          alertName: evt.alertName,
          alertFingerprint: null,
          alertRuleId: evt.alertRuleId,
          createdAt: evt.createdAt,
          updatedAt: null,
        },
        ...prev,
      ]);
    },
    onIncidentStatusChanged: (evt) => {
      setIncidents((prev) =>
        prev.map((i) =>
          i.id === evt.incidentId
            ? { ...i, status: evt.newStatus as IncidentStatus, updatedAt: evt.timestamp }
            : i
        )
      );
    },
  });

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title="事故管理"
        leading={<ShieldAlert className="h-5 w-5" />}
        actions={
          <div className="flex items-center gap-2">
            <span
              className={`h-2 w-2 rounded-full ${
                connectionState === "connected"
                  ? "bg-green-500"
                  : connectionState === "connecting" ||
                    connectionState === "reconnecting"
                  ? "bg-yellow-500"
                  : "bg-red-500"
              }`}
              title={`SignalR: ${connectionState}`}
            />
            <Button variant="outline" size="sm" onClick={fetchIncidents}>
              <RefreshCw className="mr-1 h-3.5 w-3.5" />
              刷新
            </Button>
          </div>
        }
      />

      {/* Filter bar */}
      <div className="flex items-center gap-3 border-b px-6 py-3">
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-[140px]">
            <SelectValue placeholder="状态筛选" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部状态</SelectItem>
            {INCIDENT_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>
                {s}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select value={severityFilter} onValueChange={setSeverityFilter}>
          <SelectTrigger className="w-[140px]">
            <SelectValue placeholder="严重级筛选" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部等级</SelectItem>
            {INCIDENT_SEVERITIES.map((s) => (
              <SelectItem key={s} value={s}>
                {s}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <span className="text-xs text-muted-foreground">
          共 {incidents.length} 个事故
        </span>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-6">
        {loading ? (
          <div className="flex items-center justify-center py-16">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="rounded-md bg-destructive/10 p-4 text-sm text-destructive">
            {error}
          </div>
        ) : incidents.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 text-muted-foreground">
            <ShieldAlert className="mb-2 h-10 w-10" />
            <p className="text-sm">暂无事故记录</p>
          </div>
        ) : (
          <div className="grid gap-3">
            {incidents.map((incident) => (
              <IncidentCard key={incident.id} incident={incident} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
