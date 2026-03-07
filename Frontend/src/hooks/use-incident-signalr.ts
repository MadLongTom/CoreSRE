import { useEffect, useRef, useCallback, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { HubConnectionState } from "@microsoft/signalr";
import { createIncidentHubConnection } from "@/lib/signalr";
import type {
  IncidentCreatedEvent,
  IncidentStatusChangedEvent,
  TimelineEventAddedPayload,
  ChatMessagePayload,
  IncidentResolvedEvent,
  RcaCompletedEvent,
  SopGeneratedEvent,
  AgentProcessingChangedEvent,
  HumanInterventionAcknowledgedEvent,
  IncidentTimeoutEvent,
  IncidentEscalatedEvent,
  InterventionRequestPayload,
  InterventionRequestResolvedPayload,
} from "@/types/incident";

export type SignalRConnectionState =
  | "disconnected"
  | "connecting"
  | "connected"
  | "reconnecting";

export interface IncidentSignalRCallbacks {
  // List-level events
  onIncidentCreated?: (evt: IncidentCreatedEvent) => void;
  onIncidentStatusChanged?: (evt: IncidentStatusChangedEvent) => void;
  // Detail-level events
  onTimelineEventAdded?: (evt: TimelineEventAddedPayload) => void;
  onChatMessageReceived?: (evt: ChatMessagePayload) => void;
  onIncidentResolved?: (evt: IncidentResolvedEvent) => void;
  onRcaCompleted?: (evt: RcaCompletedEvent) => void;
  onSopGenerated?: (evt: SopGeneratedEvent) => void;
  onAgentProcessingChanged?: (evt: AgentProcessingChangedEvent) => void;
  onHumanInterventionAcknowledged?: (evt: HumanInterventionAcknowledgedEvent) => void;
  onIncidentTimeout?: (evt: IncidentTimeoutEvent) => void;
  onIncidentEscalated?: (evt: IncidentEscalatedEvent) => void;
  onInterventionRequestReceived?: (evt: InterventionRequestPayload) => void;
  onInterventionRequestResolved?: (evt: InterventionRequestResolvedPayload) => void;
  // Lifecycle
  onReconnected?: () => void;
  onClose?: (error?: Error) => void;
}

/**
 * useIncidentSignalR — Incident 实时推送 hook。
 *
 * @param mode - "list" 加入列表组, "detail" 加入特定 Incident 组
 * @param incidentId - 当 mode="detail" 时必须
 * @param callbacks - 事件回调
 */
export function useIncidentSignalR(
  mode: "list" | "detail",
  incidentId: string | undefined,
  callbacks: IncidentSignalRCallbacks = {}
) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [connectionState, setConnectionState] =
    useState<SignalRConnectionState>("disconnected");
  const callbacksRef = useRef(callbacks);
  callbacksRef.current = callbacks;

  const connect = useCallback(async () => {
    if (mode === "detail" && !incidentId) return;

    // Cleanup existing
    if (connectionRef.current) {
      try {
        await connectionRef.current.stop();
      } catch {
        /* ignore */
      }
      connectionRef.current = null;
    }

    const connection = createIncidentHubConnection();
    connectionRef.current = connection;

    // ── Register event handlers BEFORE .start() ──

    // List-level
    connection.on("IncidentCreated", (evt: IncidentCreatedEvent) => {
      callbacksRef.current.onIncidentCreated?.(evt);
    });
    connection.on(
      "IncidentStatusChanged",
      (evt: IncidentStatusChangedEvent) => {
        callbacksRef.current.onIncidentStatusChanged?.(evt);
      }
    );

    // Detail-level
    connection.on(
      "TimelineEventAdded",
      (evt: TimelineEventAddedPayload) => {
        callbacksRef.current.onTimelineEventAdded?.(evt);
      }
    );
    connection.on("ChatMessageReceived", (evt: ChatMessagePayload) => {
      callbacksRef.current.onChatMessageReceived?.(evt);
    });
    connection.on("IncidentResolved", (evt: IncidentResolvedEvent) => {
      callbacksRef.current.onIncidentResolved?.(evt);
    });
    connection.on("RcaCompleted", (evt: RcaCompletedEvent) => {
      callbacksRef.current.onRcaCompleted?.(evt);
    });
    connection.on("SopGenerated", (evt: SopGeneratedEvent) => {
      callbacksRef.current.onSopGenerated?.(evt);
    });
    connection.on("AgentProcessingChanged", (evt: AgentProcessingChangedEvent) => {
      callbacksRef.current.onAgentProcessingChanged?.(evt);
    });
    connection.on("HumanInterventionAcknowledged", (evt: HumanInterventionAcknowledgedEvent) => {
      callbacksRef.current.onHumanInterventionAcknowledged?.(evt);
    });
    connection.on("IncidentTimeout", (evt: IncidentTimeoutEvent) => {
      callbacksRef.current.onIncidentTimeout?.(evt);
    });
    connection.on("IncidentEscalated", (evt: IncidentEscalatedEvent) => {
      callbacksRef.current.onIncidentEscalated?.(evt);
    });
    connection.on("InterventionRequestReceived", (evt: InterventionRequestPayload) => {
      callbacksRef.current.onInterventionRequestReceived?.(evt);
    });
    connection.on("InterventionRequestResolved", (evt: InterventionRequestResolvedPayload) => {
      callbacksRef.current.onInterventionRequestResolved?.(evt);
    });

    // Lifecycle
    connection.onreconnecting(() => setConnectionState("reconnecting"));
    connection.onreconnected(async () => {
      setConnectionState("connected");
      // Re-join group after reconnect
      try {
        if (mode === "list") {
          await connection.invoke("JoinIncidentList");
        } else if (incidentId) {
          await connection.invoke("JoinIncident", incidentId);
        }
      } catch {
        /* ignore */
      }
      callbacksRef.current.onReconnected?.();
    });
    connection.onclose((error) => {
      setConnectionState("disconnected");
      callbacksRef.current.onClose?.(error ?? undefined);
    });

    // ── Start + join group ──
    try {
      setConnectionState("connecting");
      await connection.start();
      setConnectionState("connected");

      if (mode === "list") {
        await connection.invoke("JoinIncidentList");
      } else if (incidentId) {
        await connection.invoke("JoinIncident", incidentId);
      }
    } catch (err) {
      console.error("Failed to connect to IncidentHub:", err);
      setConnectionState("disconnected");
    }
  }, [mode, incidentId]);

  useEffect(() => {
    connect();
    return () => {
      const conn = connectionRef.current;
      if (conn && conn.state !== HubConnectionState.Disconnected) {
        conn.stop();
      }
    };
  }, [connect]);

  return { connectionState };
}
