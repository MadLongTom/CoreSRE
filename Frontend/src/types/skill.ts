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
// Agent Skills Spec Constants
// ---------------------------------------------------------------------------

/** Agent Skills spec: name must be lowercase alphanumeric + hyphens, ≤64 chars */
export const SKILL_NAME_PATTERN = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/;
export const SKILL_NAME_MAX_LENGTH = 64;
export const SKILL_DESCRIPTION_MAX_LENGTH = 1024;
export const SKILL_BODY_RECOMMENDED_MAX_LINES = 500;

// ---------------------------------------------------------------------------
// Skill Registration (full detail)
// ---------------------------------------------------------------------------

export interface SkillRegistration {
  id: string;
  name: string;
  description: string;
  category: string;
  content: string;

  // Agent Skills spec fields
  license?: string | null;
  compatibility?: string | null;
  metadata?: Record<string, string> | null;
  allowedTools: string[];

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
  license?: string;
  compatibility?: string;
  metadata?: Record<string, string>;
  allowedTools?: string[];
  requiresTools?: string[];
}

export interface UpdateSkillRequest {
  name: string;
  description: string;
  category: string;
  content: string;
  license?: string | null;
  compatibility?: string | null;
  metadata?: Record<string, string> | null;
  allowedTools?: string[];
  requiresTools?: string[];
}

// ---------------------------------------------------------------------------
// Paged Response
// ---------------------------------------------------------------------------

export interface SkillPagedResponse {
  items: SkillRegistration[];
  totalCount: number;
}
