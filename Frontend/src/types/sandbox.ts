// =============================================================================
// Sandbox Type Definitions — maps to backend C# DTOs
// =============================================================================

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export type SandboxStatus = "Creating" | "Running" | "Stopped" | "Terminated";

export const SANDBOX_STATUSES: SandboxStatus[] = [
  "Creating",
  "Running",
  "Stopped",
  "Terminated",
];

export type SandboxMode = "None" | "Ephemeral" | "Persistent";

export const SANDBOX_MODES: SandboxMode[] = ["None", "Ephemeral", "Persistent"];

// ---------------------------------------------------------------------------
// Sandbox Instance (full detail)
// ---------------------------------------------------------------------------

export interface SandboxInstance {
  id: string;
  name: string;
  status: SandboxStatus;
  sandboxType: string;
  image: string;
  cpuCores: number;
  memoryMib: number;
  k8sNamespace: string;
  autoStopMinutes: number;
  persistWorkspace: boolean;
  agentId?: string | null;
  lastActivityAt?: string | null;
  podName?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

// ---------------------------------------------------------------------------
// Exec Result
// ---------------------------------------------------------------------------

export interface SandboxExecResult {
  exitCode: number;
  stdout: string;
  stderr: string;
}

// ---------------------------------------------------------------------------
// Request Types
// ---------------------------------------------------------------------------

export interface CreateSandboxRequest {
  name: string;
  sandboxType?: string;
  image?: string;
  cpuCores?: number;
  memoryMib?: number;
  k8sNamespace?: string;
  autoStopMinutes?: number;
  persistWorkspace?: boolean;
  agentId?: string;
}

export interface UpdateSandboxRequest {
  name?: string;
  image?: string;
  cpuCores?: number;
  memoryMib?: number;
  autoStopMinutes?: number;
  persistWorkspace?: boolean;
  agentId?: string;
}

export interface ExecSandboxRequest {
  command: string;
  args?: string[];
}

// ---------------------------------------------------------------------------
// Paged Response
// ---------------------------------------------------------------------------

export interface SandboxPagedResponse {
  items: SandboxInstance[];
  totalCount: number;
}
