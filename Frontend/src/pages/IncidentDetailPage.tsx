import { useCallback, useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router";
import { PageHeader } from "@/components/layout/PageHeader";
import { IncidentTimeline } from "@/components/incidents/IncidentTimeline";
import { IncidentChatPanel } from "@/components/incidents/IncidentChatPanel";
import { IncidentContextPanel } from "@/components/incidents/IncidentContextPanel";
import { IncidentSeverityBadge } from "@/components/incidents/IncidentSeverityBadge";
import { IncidentStatusBadge } from "@/components/incidents/IncidentStatusBadge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { useIncidentSignalR } from "@/hooks/use-incident-signalr";
import {
  ArrowLeft,
  Loader2,
  CheckCircle,
  AlertTriangle,
} from "lucide-react";
import type {
  IncidentDetail,
  IncidentTimelineItem,
  ChatMessagePayload,
  IncidentStatus,
} from "@/types/incident";
import type { ApiResult } from "@/types/agent";

export default function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [incident, setIncident] = useState<IncidentDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chatMessages, setChatMessages] = useState<ChatMessagePayload[]>([]);

  const fetchIncident = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const resp = await fetch(`/api/incidents/${id}`);
      const result: ApiResult<IncidentDetail> = await resp.json();
      if (result.success && result.data) {
        setIncident(result.data);
      } else {
        setError(result.error ?? "事故未找到");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Network error");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    fetchIncident();
  }, [fetchIncident]);

  // SignalR: real-time updates for this incident
  const { connectionState } = useIncidentSignalR("detail", id, {
    onTimelineEventAdded: (evt) => {
      if (evt.incidentId !== id) return;
      const newItem: IncidentTimelineItem = {
        eventType: evt.eventType as IncidentTimelineItem["eventType"],
        summary: evt.summary,
        timestamp: evt.timestamp,
        actorAgentId: evt.actorAgentId,
        metadata: evt.metadata,
      };
      setIncident((prev) =>
        prev ? { ...prev, timeline: [...prev.timeline, newItem] } : prev
      );
    },
    onChatMessageReceived: (evt) => {
      if (evt.incidentId !== id) return;
      setChatMessages((prev) => [...prev, evt]);
    },
    onIncidentStatusChanged: (evt) => {
      if (evt.incidentId !== id) return;
      setIncident((prev) =>
        prev
          ? { ...prev, status: evt.newStatus as IncidentStatus, updatedAt: evt.timestamp }
          : prev
      );
    },
    onIncidentResolved: (evt) => {
      if (evt.incidentId !== id) return;
      setIncident((prev) =>
        prev
          ? {
              ...prev,
              status: "Resolved" as IncidentStatus,
              resolution: evt.resolution,
              updatedAt: evt.resolvedAt,
            }
          : prev
      );
    },
    onRcaCompleted: (evt) => {
      if (evt.incidentId !== id) return;
      setIncident((prev) =>
        prev ? { ...prev, rootCause: evt.rootCause } : prev
      );
    },
    onSopGenerated: (evt) => {
      if (evt.incidentId !== id) return;
      setIncident((prev) =>
        prev ? { ...prev, generatedSopId: evt.skillId } : prev
      );
    },
  });

  // ── Actions ──
  const handleResolve = async () => {
    if (!id) return;
    await fetch(`/api/incidents/${id}/status`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ newStatus: "Resolved", note: "手动标记已解决" }),
    });
    fetchIncident();
  };

  const handleEscalate = async () => {
    if (!id) return;
    await fetch(`/api/incidents/${id}/status`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ newStatus: "Escalated", note: "手动上报" }),
    });
    fetchIncident();
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error || !incident) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3">
        <p className="text-sm text-destructive">{error ?? "未知错误"}</p>
        <Button variant="outline" onClick={() => navigate("/incidents")}>
          返回列表
        </Button>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <PageHeader
        title={incident.title}
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/incidents")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
        actions={
          <div className="flex items-center gap-2">
            <span
              className={`h-2 w-2 rounded-full ${
                connectionState === "connected"
                  ? "bg-green-500"
                  : "bg-yellow-500"
              }`}
              title={`SignalR: ${connectionState}`}
            />
            <IncidentSeverityBadge severity={incident.severity} />
            <IncidentStatusBadge status={incident.status} />
            {incident.status !== "Resolved" &&
              incident.status !== "Closed" && (
                <>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handleResolve}
                  >
                    <CheckCircle className="mr-1 h-3.5 w-3.5" />
                    标记已解决
                  </Button>
                  <Button
                    variant="destructive"
                    size="sm"
                    onClick={handleEscalate}
                  >
                    <AlertTriangle className="mr-1 h-3.5 w-3.5" />
                    上报
                  </Button>
                </>
              )}
          </div>
        }
      />

      {/* Three-column layout */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left: Timeline */}
        <div className="w-72 shrink-0 overflow-y-auto border-r p-4">
          <h3 className="mb-3 text-sm font-semibold">时间线</h3>
          <IncidentTimeline items={incident.timeline} />
        </div>

        {/* Center: Chat */}
        <div className="flex flex-1 flex-col overflow-hidden">
          <div className="border-b px-4 py-2 text-xs text-muted-foreground">
            Agent 对话
          </div>
          <div className="flex-1 overflow-y-auto">
            <IncidentChatPanel messages={chatMessages} />
          </div>
        </div>

        {/* Right: Context Panel */}
        <div className="w-80 shrink-0 overflow-y-auto border-l">
          <IncidentContextPanel incident={incident} />
        </div>
      </div>
    </div>
  );
}
