import { useCallback, useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router";
import { PageHeader } from "@/components/layout/PageHeader";
import { IncidentTimeline } from "@/components/incidents/IncidentTimeline";
import { IncidentChatPanel } from "@/components/incidents/IncidentChatPanel";
import { IncidentContextPanel } from "@/components/incidents/IncidentContextPanel";
import { IncidentSeverityBadge } from "@/components/incidents/IncidentSeverityBadge";
import { IncidentStatusBadge } from "@/components/incidents/IncidentStatusBadge";
import { PostMortemPanel } from "@/components/incidents/PostMortemPanel";
import { StepExecutionPanel } from "@/components/incidents/StepExecutionPanel";
import { Button } from "@/components/ui/button";
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
  InterventionRequestPayload,
} from "@/types/incident";
import type { ApiResult } from "@/types/agent";

export default function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [incident, setIncident] = useState<IncidentDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chatMessages, setChatMessages] = useState<ChatMessagePayload[]>([]);
  const [isAgentProcessing, setIsAgentProcessing] = useState(false);
  const [processingAgentName, setProcessingAgentName] = useState<string | null>(null);
  const [pendingRequests, setPendingRequests] = useState<InterventionRequestPayload[]>([]);

  const fetchIncident = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const resp = await fetch(`/api/incidents/${id}`);
      const result: ApiResult<IncidentDetail> = await resp.json();
      if (result.success && result.data) {
        setIncident(result.data);
      }  else {
        setError(result.message ?? "事故未找到");
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

  // Check active agent processing status on load
  useEffect(() => {
    if (!id) return;
    fetch(`/api/incidents/${id}/active`)
      .then((r) => r.json())
      .then((data: { isActive: boolean }) => {
        setIsAgentProcessing(data.isActive);
      })
      .catch(() => {});

    // Load existing chat history
    fetch(`/api/incidents/${id}/chat`)
      .then((r) => r.json())
      .then((result: ApiResult<ChatMessagePayload[]>) => {
        if (result.success && result.data && result.data.length > 0) {
          setChatMessages(result.data);
        }
      })
      .catch(() => {});
  }, [id]);

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
    onAgentProcessingChanged: (evt) => {
      if (evt.incidentId !== id) return;
      setIsAgentProcessing(evt.isProcessing);
      setProcessingAgentName(evt.agentName);
    },
    onInterventionRequestReceived: (evt) => {
      if (evt.incidentId !== id) return;
      setPendingRequests((prev) => [...prev, evt]);
    },
    onInterventionRequestResolved: (evt) => {
      if (evt.incidentId !== id) return;
      setPendingRequests((prev) =>
        prev.filter((r) => r.requestId !== evt.requestId)
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
            {isAgentProcessing && (
              <span className="flex items-center gap-1 rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-700">
                <Loader2 className="h-3 w-3 animate-spin" />
                Agent 处理中
              </span>
            )}
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
            {isAgentProcessing && processingAgentName && (
              <span className="ml-2 text-purple-600">
                — {processingAgentName}
              </span>
            )}
          </div>
          <div className="flex-1 overflow-hidden">
            <IncidentChatPanel
              messages={chatMessages}
              incidentId={id!}
              isAgentProcessing={isAgentProcessing}
              agentName={processingAgentName}
              isResolved={incident.status === "Resolved" || incident.status === "Closed"}
              pendingRequests={pendingRequests}
            />
          </div>
        </div>

        {/* Right: Context Panel */}
        <div className="w-80 shrink-0 overflow-y-auto border-l">
          <IncidentContextPanel incident={incident} />

          {/* Spec 024 — Step Execution */}
          {incident.sopSteps && incident.sopSteps.length > 0 && (
            <div className="p-4">
              <StepExecutionPanel
                incidentId={id!}
                steps={incident.sopSteps}
                executions={incident.stepExecutions ?? []}
                onRefresh={fetchIncident}
              />
            </div>
          )}

          {/* Spec 025 — Fallback indicator */}
          {incident.route === "FallbackRca" && incident.fallbackReason && (
            <div className="mx-4 mb-3 rounded-md border border-orange-200 bg-orange-50 p-2 text-xs text-orange-800">
              <p className="font-medium">已降级为根因分析</p>
              <p className="text-muted-foreground mt-0.5">
                原因: {incident.fallbackReason}
              </p>
            </div>
          )}

          {/* Spec 023 — Post-mortem */}
          {(incident.status === "Resolved" || incident.status === "Closed") && (
            <div className="p-4">
              <PostMortemPanel
                incidentId={id!}
                route={incident.route}
                postMortem={incident.postMortem ?? null}
                onRefresh={fetchIncident}
              />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
