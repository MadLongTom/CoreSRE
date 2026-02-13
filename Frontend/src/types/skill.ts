// =============================================================================
// Skill Type Definitions — maps to backend C# DTOs
// =============================================================================

import type { ApiResult } from "@/types/agent";
export type { ApiResult };

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export type SkillScope = "Builtin" | "User" | "Project";

export const SKILL_SCOPES: SkillScope[] = ["Builtin", "User", "Project"];

export type SkillStatus = "Active" | "Inactive";

export const SKILL_STATUSES: SkillStatus[] = ["Active", "Inactive"];

// ---------------------------------------------------------------------------
// Skill Registration (full detail)
// ---------------------------------------------------------------------------

export interface SkillRegistration {
  id: string;
  name: string;
  description: string;
  category: string;
  content: string;
  scope: SkillScope;
  status: SkillStatus;
  requiresTools: string[];
  hasFiles: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

// ---------------------------------------------------------------------------
// Skill File Entry
// ---------------------------------------------------------------------------

export interface SkillFileEntry {
  key: string;
  size: number;
  lastModified: string;
}

// ---------------------------------------------------------------------------
// Request Types
// ---------------------------------------------------------------------------

export interface CreateSkillRequest {
  name: string;
  description: string;
  category: string;
  content: string;
  scope?: string;
  requiresTools?: string[];
}

export interface UpdateSkillRequest {
  name: string;
  description: string;
  category: string;
  content: string;
  requiresTools?: string[];
}

// ---------------------------------------------------------------------------
// Paged Response
// ---------------------------------------------------------------------------

export interface SkillPagedResponse {
  items: SkillRegistration[];
  totalCount: number;
}
