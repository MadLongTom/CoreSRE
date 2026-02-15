import { useEffect, useRef, useCallback, useState } from "react";
import { HubConnection, HubConnectionState } from "@microsoft/signalr";
import { createWorkflowHubConnection } from "@/lib/signalr";
import type { NodeExecution, NodeExecutionStatus, ExecutionStatus } from "@/types/workflow";

/** SignalR 连接状态 */
export type SignalRConnectionState =
  | "disconnected"
  | "connecting"
  | "connected"
  | "reconnecting";

export interface WorkflowSignalRCallbacks {
  /** 当工作流执行开始时触发 */
  onExecutionStarted?: (executionId: string, workflowDefinitionId: string) => void;
  /** 当节点开始执行时触发 */
  onNodeExecutionStarted?: (executionId: string, nodeId: string, input: string | null) => void;
  /** 当节点执行成功完成时触发 */
  onNodeExecutionCompleted?: (executionId: string, nodeId: string, output: string | null) => void;
  /** 当节点执行失败时触发 */
  onNodeExecutionFailed?: (executionId: string, nodeId: string, error: string) => void;
  /** 当节点被跳过时触发 */
  onNodeExecutionSkipped?: (executionId: string, nodeId: string) => void;
  /** 当工作流执行成功完成时触发 */
  onExecutionCompleted?: (executionId: string, output: string | null) => void;
  /** 当工作流执行失败时触发 */
  onExecutionFailed?: (executionId: string, error: string) => void;
  /** 当重连成功时触发（用于 REST 状态恢复） */
  onReconnected?: () => void;
  /** 当连接永久关闭时触发（用于降级 REST 轮询） */
  onClose?: () => void;
}

/**
 * 工作流 SignalR 实时推送 hook。
 * 管理连接生命周期，注册 7 个事件处理程序，自动加入/离开执行组。
 */
export function useWorkflowSignalR(
  executionId: string | undefined,
  callbacks: WorkflowSignalRCallbacks = {}
) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [connectionState, setConnectionState] = useState<SignalRConnectionState>("disconnected");

  // Use refs for callbacks to avoid re-triggering effect
  const callbacksRef = useRef(callbacks);
  callbacksRef.current = callbacks;

  const connect = useCallback(async () => {
    if (!executionId) return;

    // Clean up existing connection
    if (connectionRef.current) {
      try {
        await connectionRef.current.stop();
      } catch {
        // ignore
      }
    }

    const connection = createWorkflowHubConnection();
    connectionRef.current = connection;

    // Register 7 client event handlers BEFORE starting
    connection.on("ExecutionStarted", (execId: string, workflowDefId: string) => {
      callbacksRef.current.onExecutionStarted?.(execId, workflowDefId);
    });

    connection.on("NodeExecutionStarted", (execId: string, nodeId: string, input: string | null) => {
      callbacksRef.current.onNodeExecutionStarted?.(execId, nodeId, input);
    });

    connection.on("NodeExecutionCompleted", (execId: string, nodeId: string, output: string | null) => {
      callbacksRef.current.onNodeExecutionCompleted?.(execId, nodeId, output);
    });

    connection.on("NodeExecutionFailed", (execId: string, nodeId: string, error: string) => {
      callbacksRef.current.onNodeExecutionFailed?.(execId, nodeId, error);
    });

    connection.on("NodeExecutionSkipped", (execId: string, nodeId: string) => {
      callbacksRef.current.onNodeExecutionSkipped?.(execId, nodeId);
    });

    connection.on("ExecutionCompleted", (execId: string, output: string | null) => {
      callbacksRef.current.onExecutionCompleted?.(execId, output);
    });

    connection.on("ExecutionFailed", (execId: string, error: string) => {
      callbacksRef.current.onExecutionFailed?.(execId, error);
    });

    // Connection lifecycle callbacks
    connection.onreconnecting(() => {
      setConnectionState("reconnecting");
    });

    connection.onreconnected(async () => {
      setConnectionState("connected");
      // Re-join the execution group after reconnection
      try {
        await connection.invoke("JoinExecution", executionId);
      } catch (err) {
        console.error("Failed to re-join execution group after reconnect", err);
      }
      callbacksRef.current.onReconnected?.();
    });

    connection.onclose(() => {
      setConnectionState("disconnected");
      callbacksRef.current.onClose?.();
    });

    // Start connection and join group
    setConnectionState("connecting");
    try {
      await connection.start();
      setConnectionState("connected");
      await connection.invoke("JoinExecution", executionId);
    } catch (err) {
      console.error("SignalR connection failed", err);
      setConnectionState("disconnected");
    }
  }, [executionId]);

  useEffect(() => {
    connect();

    return () => {
      const conn = connectionRef.current;
      if (conn && conn.state !== HubConnectionState.Disconnected) {
        conn.stop().catch(() => {});
      }
      connectionRef.current = null;
    };
  }, [connect]);

  return { connectionState };
}

/**
 * 工具函数：根据 SignalR 事件更新节点执行列表。
 * 用于在 WorkflowExecutionDetailPage 中合并实时事件到已有 nodeExecutions。
 */
export function applyNodeEvent(
  nodeExecutions: NodeExecution[],
  nodeId: string,
  status: NodeExecutionStatus,
  extra?: { input?: string | null; output?: string | null; error?: string | null }
): NodeExecution[] {
  const existing = nodeExecutions.find((ne) => ne.nodeId === nodeId);
  if (existing) {
    return nodeExecutions.map((ne) =>
      ne.nodeId === nodeId
        ? {
            ...ne,
            status,
            ...(extra?.input !== undefined ? { input: extra.input } : {}),
            ...(extra?.output !== undefined ? { output: extra.output } : {}),
            ...(extra?.error !== undefined ? { errorMessage: extra.error } : {}),
            ...(status === "Running" ? { startedAt: new Date().toISOString() } : {}),
            ...(status === "Completed" || status === "Failed" || status === "Skipped"
              ? { completedAt: new Date().toISOString() }
              : {}),
          }
        : ne
    );
  }
  // New node execution entry
  return [
    ...nodeExecutions,
    {
      nodeId,
      status,
      input: extra?.input ?? null,
      output: extra?.output ?? null,
      errorMessage: extra?.error ?? null,
      startedAt: status === "Running" ? new Date().toISOString() : null,
      completedAt:
        status === "Completed" || status === "Failed" || status === "Skipped"
          ? new Date().toISOString()
          : null,
    },
  ];
}

/**
 * 工具函数：根据 SignalR 执行事件更新执行状态。
 */
export function applyExecutionStatusEvent(
  event: "started" | "completed" | "failed"
): ExecutionStatus {
  switch (event) {
    case "started":
      return "Running";
    case "completed":
      return "Completed";
    case "failed":
      return "Failed";
  }
}
