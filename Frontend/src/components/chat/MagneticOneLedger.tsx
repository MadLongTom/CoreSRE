import { useMemo, useState } from "react";
import type { OuterLedger, InnerLedgerEntry, OrchestratorMessage, OrchestratorThought } from "@/types/chat";
import {
  ChevronRight, ChevronDown, CheckCircle2, Loader2, XCircle,
  BookOpen, ListChecks, MessageSquare, AlertTriangle, Brain,
  Clock, Circle,
} from "lucide-react";

// ── Parsed JSON types for structured display ──────────────────────────

interface PlanStep {
  step_number: number;
  description: string;
  assigned_agent: string;
}

interface FactsData {
  given?: string[];
  to_look_up?: string[];
  to_derive?: string[];
  guess?: string[];
}

interface ProgressLedgerData {
  is_request_satisfied: boolean;
  is_request_satisfied_reason: string;
  is_in_loop: boolean;
  is_progress_being_made: boolean;
  next_speaker: string;
  instruction_or_question: string;
}

// ── Helpers ───────────────────────────────────────────────────────────

/** Strip markdown ` ```json ... ``` ` fences from LLM output. */
function extractJson(text: string): string {
  const fenced = text.match(/```(?:json)?\s*\n?([\s\S]*?)```/);
  return fenced ? fenced[1].trim() : text.trim();
}

function tryParseJson<T>(raw: string): T | null {
  try {
    return JSON.parse(extractJson(raw)) as T;
  } catch {
    return null;
  }
}

/** Get the latest plan steps from orchestrator thoughts. */
function parseLatestPlan(thoughts: OrchestratorThought[]): PlanStep[] {
  const planThoughts = thoughts.filter(t => t.category === "plan" || t.category === "plan_update");
  if (planThoughts.length === 0) return [];
  const latest = planThoughts[planThoughts.length - 1];
  const data = tryParseJson<{ steps?: PlanStep[] }>(latest.content);
  return data?.steps ?? [];
}

/**
 * Determine plan step status by matching against orchestrator decisions.
 * - If a non-final decision dispatched to this step's agent → completed
 * - If the final decision dispatched to this step's agent → in-progress
 * - Otherwise → pending
 */
function getStepStatus(
  step: PlanStep,
  decisions: OrchestratorMessage[],
): "completed" | "active" | "pending" {
  if (decisions.length === 0) return "pending";
  const lastIdx = decisions.length - 1;
  // Check if any earlier (completed) decision targeted this agent
  for (let i = 0; i < lastIdx; i++) {
    if (decisions[i].targetAgent === step.assigned_agent) return "completed";
  }
  // Check the last decision
  const last = decisions[lastIdx];
  if (last.targetAgent === step.assigned_agent) {
    return last.isRequestSatisfied ? "completed" : "active";
  }
  return "pending";
}

/**
 * Decision-level status — based on position in the list, not internal flags.
 * - Decisions before the last → step was executed → "completed"
 * - Last decision + isRequestSatisfied → "awaiting" human input
 * - Last decision + isInLoop → "in-loop"
 * - Last decision otherwise → "in-progress"
 */
function getDecisionStatus(
  msg: OrchestratorMessage,
  index: number,
  all: OrchestratorMessage[],
): "completed" | "awaiting" | "in-loop" | "in-progress" {
  if (msg.isRequestSatisfied) return "awaiting";
  if (index < all.length - 1) return "completed";
  if (msg.isInLoop) return "in-loop";
  return "in-progress";
}

// ── Props ─────────────────────────────────────────────────────────────

interface MagneticOneLedgerProps {
  outerLedger: OuterLedger | null;
  innerLedgerEntries: InnerLedgerEntry[];
  orchestratorMessages: OrchestratorMessage[];
  orchestratorThoughts: OrchestratorThought[];
}

// ── Main Component ────────────────────────────────────────────────────

/**
 * MagneticOneLedger — collapsible side panel for MagneticOne mode.
 *
 * Sections (top → bottom):
 * 1. Task Plan — Todo-style progress panel parsed from the latest plan thought
 * 2. Status — outer ledger progress / stalls / completion from OuterLedger
 * 3. Orchestrator Decisions — per-step dispatch with position-based status icons
 * 4. LLM Insights — structured cards for Facts, ProgressLedger, FinalAnswer
 * 5. Agent Task Log — inner ledger entries
 */
export function MagneticOneLedger({ outerLedger, innerLedgerEntries, orchestratorMessages, orchestratorThoughts }: MagneticOneLedgerProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [showOrchestrator, setShowOrchestrator] = useState(true);
  const [showThoughts, setShowThoughts] = useState(true);

  // Parse the latest plan into structured steps
  const planSteps = useMemo(() => parseLatestPlan(orchestratorThoughts), [orchestratorThoughts]);

  // Separate thought categories for structured rendering
  const nonPlanThoughts = useMemo(
    () => orchestratorThoughts.filter(t => t.category !== "plan" && t.category !== "plan_update"),
    [orchestratorThoughts],
  );

  if (!outerLedger && innerLedgerEntries.length === 0 && orchestratorMessages.length === 0 && orchestratorThoughts.length === 0) {
    return (
      <div className="flex w-80 flex-col border-l bg-muted/20 p-4 text-sm text-muted-foreground">
        <div className="flex items-center gap-2 font-medium text-foreground">
          <BookOpen className="h-4 w-4 text-purple-500" />
          MagneticOne Ledger
        </div>
        <p className="mt-3 text-xs">Waiting for orchestrator to begin planning…</p>
      </div>
    );
  }

  return (
    <div className="flex w-80 flex-col border-l bg-muted/20 overflow-y-auto">
      {/* Header with collapse toggle */}
      <button
        onClick={() => setIsCollapsed(!isCollapsed)}
        className="flex items-center gap-2 border-b px-4 py-3 text-sm font-medium text-foreground hover:bg-muted/40 transition-colors"
      >
        {isCollapsed ? (
          <ChevronRight className="h-4 w-4" />
        ) : (
          <ChevronDown className="h-4 w-4" />
        )}
        <BookOpen className="h-4 w-4 text-purple-500" />
        <span className="flex-1 text-left">MagneticOne Ledger</span>
        {outerLedger && outerLedger.iteration > 0 && (
          <span className="ml-auto rounded-full bg-purple-100 px-2 py-0.5 text-[10px] font-semibold text-purple-700 dark:bg-purple-900/40 dark:text-purple-300">
            Iter {outerLedger.iteration}
          </span>
        )}
      </button>

      {!isCollapsed && (
        <div className="flex flex-1 flex-col overflow-y-auto">

          {/* ── 1. Task Plan — Todo-style progress panel ─────────── */}
          {planSteps.length > 0 && (
            <div className="border-b p-4 space-y-2">
              <div className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                <ListChecks className="h-3.5 w-3.5" />
                <span>Task Plan</span>
              </div>
              {planSteps.map((step) => {
                const status = getStepStatus(step, orchestratorMessages);
                return (
                  <div key={step.step_number} className="flex items-start gap-2 text-xs">
                    <span className="mt-0.5 shrink-0">
                      {status === "completed" ? (
                        <CheckCircle2 className="h-3.5 w-3.5 text-green-500" />
                      ) : status === "active" ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin text-blue-500" />
                      ) : (
                        <Circle className="h-3.5 w-3.5 text-muted-foreground/40" />
                      )}
                    </span>
                    <div className="flex-1 min-w-0">
                      <p className={`leading-relaxed ${status === "completed" ? "text-muted-foreground line-through" : "text-foreground"}`}>
                        {step.description}
                      </p>
                      <span className="inline-block mt-0.5 rounded bg-purple-100 px-1.5 py-0.5 text-[10px] font-medium text-purple-700 dark:bg-purple-900/40 dark:text-purple-300">
                        {step.assigned_agent}
                      </span>
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {/* ── 2. Status — Outer Ledger progress / stalls / completion ── */}
          {outerLedger && (
            <div className="border-b p-4 space-y-3">
              <div className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                <ListChecks className="h-3.5 w-3.5" />
                <span className="flex-1">Status</span>
                {outerLedger.nStalls > 0 && (
                  <span className={`flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[10px] font-semibold ${
                    outerLedger.nStalls >= outerLedger.maxStalls
                      ? "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300"
                      : "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300"
                  }`}>
                    <AlertTriangle className="h-3 w-3" />
                    {outerLedger.nStalls}/{outerLedger.maxStalls} stalls
                  </span>
                )}
              </div>

              {outerLedger.nextStep && (
                <div>
                  <p className="text-xs font-medium text-muted-foreground mb-1">Next Step</p>
                  <p className="text-xs whitespace-pre-wrap rounded bg-purple-50 px-2 py-1.5 text-purple-800 dark:bg-purple-900/30 dark:text-purple-200">
                    {outerLedger.nextStep}
                  </p>
                </div>
              )}

              {outerLedger.progress && (
                <div>
                  <p className="text-xs font-medium text-muted-foreground mb-1">Progress</p>
                  <p className="text-xs whitespace-pre-wrap leading-relaxed">{outerLedger.progress}</p>
                </div>
              )}

              {outerLedger.isComplete && (
                <div className="space-y-2">
                  <div className="flex items-center gap-1.5 rounded bg-green-50 px-2 py-1.5 text-xs font-medium text-green-700 dark:bg-green-900/30 dark:text-green-300">
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    Task Complete
                  </div>
                  {outerLedger.finalAnswer && (
                    <div>
                      <p className="text-xs font-medium text-muted-foreground mb-1">Final Answer</p>
                      <p className="text-xs whitespace-pre-wrap rounded bg-green-50 px-2 py-1.5 text-green-900 dark:bg-green-900/20 dark:text-green-100 leading-relaxed">
                        {outerLedger.finalAnswer}
                      </p>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* ── 3. Orchestrator Decisions — per-step with position-based status ── */}
          {orchestratorMessages.length > 0 && (
            <div className="border-b">
              <button
                onClick={() => setShowOrchestrator(!showOrchestrator)}
                className="flex w-full items-center gap-1.5 px-4 py-2.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground hover:bg-muted/40 transition-colors"
              >
                {showOrchestrator ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                <MessageSquare className="h-3.5 w-3.5" />
                <span>Orchestrator Decisions ({orchestratorMessages.length})</span>
              </button>
              {showOrchestrator && (
                <div className="px-4 pb-3 space-y-2 max-h-60 overflow-y-auto">
                  {orchestratorMessages.map((msg, i) => {
                    const status = getDecisionStatus(msg, i, orchestratorMessages);
                    return (
                      <div
                        key={`orch-${msg.iteration}-${i}`}
                        className="rounded border bg-background p-2 space-y-1"
                      >
                        <div className="flex items-center gap-1.5 text-[10px]">
                          <span className="font-semibold text-purple-600 dark:text-purple-400">
                            #{msg.iteration}
                          </span>
                          <span className="text-muted-foreground">→</span>
                          <span className="font-medium">{msg.targetAgent}</span>
                          <span className="ml-auto flex items-center gap-1">
                            <DecisionStatusIcon status={status} />
                          </span>
                        </div>
                        <p className="text-[11px] text-foreground/80 leading-relaxed line-clamp-3">
                          {msg.instruction}
                        </p>
                        {msg.reason && (
                          <p className="text-[10px] text-muted-foreground italic line-clamp-2">
                            {msg.reason}
                          </p>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* ── 4. LLM Insights — structured cards for Facts / ProgressLedger / FinalAnswer ── */}
          {nonPlanThoughts.length > 0 && (
            <div className="border-b">
              <button
                onClick={() => setShowThoughts(!showThoughts)}
                className="flex w-full items-center gap-1.5 px-4 py-2.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground hover:bg-muted/40 transition-colors"
              >
                {showThoughts ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                <Brain className="h-3.5 w-3.5" />
                <span>LLM Insights ({nonPlanThoughts.length})</span>
              </button>
              {showThoughts && (
                <div className="px-4 pb-3 space-y-2 max-h-80 overflow-y-auto">
                  {nonPlanThoughts.map((thought, i) => (
                    <ThoughtCard key={`thought-${i}`} thought={thought} index={i} />
                  ))}
                </div>
              )}
            </div>
          )}

          {/* ── 5. Agent Task Log ──────────────────────────────────── */}
          {innerLedgerEntries.length > 0 && (
            <div className="flex-1 p-4 space-y-2">
              <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-2">
                Agent Task Log ({innerLedgerEntries.length})
              </p>
              {innerLedgerEntries.map((entry, i) => (
                <div
                  key={`${entry.agentName}-${entry.timestamp}-${i}`}
                  className="rounded border bg-background p-2.5 space-y-1"
                >
                  <div className="flex items-center gap-1.5">
                    <AgentStatusIcon status={entry.status} />
                    <span className="text-xs font-medium">{entry.agentName}</span>
                    <span className="ml-auto text-[10px] text-muted-foreground">
                      {entry.status}
                    </span>
                  </div>
                  {entry.task && (
                    <p className="text-xs text-muted-foreground line-clamp-2">{entry.task}</p>
                  )}
                  {entry.summary && (
                    <p className="text-xs text-foreground/80">{entry.summary}</p>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ── Sub-components ────────────────────────────────────────────────────

function DecisionStatusIcon({ status }: { status: ReturnType<typeof getDecisionStatus> }) {
  switch (status) {
    case "completed":
      return <CheckCircle2 className="h-3 w-3 text-green-500" />;
    case "awaiting":
      return <Clock className="h-3 w-3 text-amber-500" />;
    case "in-loop":
      return <AlertTriangle className="h-3 w-3 text-yellow-500" />;
    case "in-progress":
      return <Loader2 className="h-3 w-3 animate-spin text-blue-500" />;
  }
}

function AgentStatusIcon({ status }: { status: InnerLedgerEntry["status"] }) {
  switch (status) {
    case "running":
      return <Loader2 className="h-3.5 w-3.5 animate-spin text-blue-500" />;
    case "completed":
      return <CheckCircle2 className="h-3.5 w-3.5 text-green-500" />;
    case "failed":
      return <XCircle className="h-3.5 w-3.5 text-red-500" />;
  }
}

// ── Thought Cards — structured rendering by category ──────────────────

const THOUGHT_LABELS: Record<string, string> = {
  facts: "Facts",
  facts_update: "Facts Update",
  progress_ledger: "Progress Ledger",
  final_answer: "Final Answer",
};

const THOUGHT_COLORS: Record<string, string> = {
  facts: "bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300",
  facts_update: "bg-sky-100 text-sky-700 dark:bg-sky-900/40 dark:text-sky-300",
  progress_ledger: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  final_answer: "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
};

function ThoughtCard({ thought, index }: { thought: OrchestratorThought; index: number }) {
  const [expanded, setExpanded] = useState(false);
  const label = THOUGHT_LABELS[thought.category] ?? thought.category;
  const colorClasses = THOUGHT_COLORS[thought.category] ?? "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300";

  return (
    <div className="rounded border bg-background">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center gap-1.5 px-2 py-1.5 text-left hover:bg-muted/40 transition-colors"
      >
        {expanded ? <ChevronDown className="h-3 w-3 shrink-0" /> : <ChevronRight className="h-3 w-3 shrink-0" />}
        <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${colorClasses}`}>
          {label}
        </span>
        <span className="text-[10px] text-muted-foreground ml-auto">#{index + 1}</span>
      </button>
      {expanded && (
        <div className="border-t px-2 py-1.5">
          <StructuredContent category={thought.category} content={thought.content} />
        </div>
      )}
    </div>
  );
}

/** Render structured JSON content based on thought category. */
function StructuredContent({ category, content }: { category: string; content: string }) {
  if (category === "facts" || category === "facts_update") {
    const data = tryParseJson<FactsData>(content);
    if (data) return <FactsCard data={data} />;
  }
  if (category === "progress_ledger") {
    const data = tryParseJson<ProgressLedgerData>(content);
    if (data) return <ProgressLedgerCard data={data} />;
  }
  // Fallback: raw text
  return (
    <pre className="text-[11px] whitespace-pre-wrap break-words leading-relaxed text-foreground/80 max-h-60 overflow-y-auto">
      {content}
    </pre>
  );
}

const FACTS_SECTIONS: { key: keyof FactsData; label: string; color: string }[] = [
  { key: "given", label: "Given", color: "text-green-600 dark:text-green-400" },
  { key: "to_look_up", label: "To Look Up", color: "text-blue-600 dark:text-blue-400" },
  { key: "to_derive", label: "To Derive", color: "text-purple-600 dark:text-purple-400" },
  { key: "guess", label: "Guess", color: "text-amber-600 dark:text-amber-400" },
];

function FactsCard({ data }: { data: FactsData }) {
  return (
    <div className="space-y-2">
      {FACTS_SECTIONS.map(({ key, label, color }) => {
        const items = data[key];
        if (!items || items.length === 0) return null;
        return (
          <div key={key}>
            <p className={`text-[10px] font-semibold uppercase tracking-wider mb-0.5 ${color}`}>{label}</p>
            <ul className="space-y-0.5">
              {items.map((item, i) => (
                <li key={i} className="text-[11px] text-foreground/80 leading-relaxed flex gap-1.5">
                  <span className="shrink-0 text-muted-foreground">•</span>
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </div>
  );
}

function ProgressLedgerCard({ data }: { data: ProgressLedgerData }) {
  return (
    <div className="space-y-1.5 text-[11px]">
      <div className="flex items-center gap-1.5">
        <span className="font-medium text-muted-foreground">Satisfied:</span>
        {data.is_request_satisfied ? (
          <span className="flex items-center gap-1 text-green-600 dark:text-green-400">
            <CheckCircle2 className="h-3 w-3" /> Yes
          </span>
        ) : (
          <span className="flex items-center gap-1 text-amber-600 dark:text-amber-400">
            <Clock className="h-3 w-3" /> No
          </span>
        )}
      </div>
      <div className="flex items-center gap-1.5">
        <span className="font-medium text-muted-foreground">Progress:</span>
        {data.is_progress_being_made ? (
          <span className="text-blue-600 dark:text-blue-400">Yes</span>
        ) : (
          <span className="text-red-600 dark:text-red-400">No</span>
        )}
      </div>
      {data.is_in_loop && (
        <div className="flex items-center gap-1.5 text-yellow-600 dark:text-yellow-400">
          <AlertTriangle className="h-3 w-3" />
          <span className="font-medium">In Loop</span>
        </div>
      )}
      <div>
        <span className="font-medium text-muted-foreground">Next:</span>{" "}
        <span className="font-medium text-purple-600 dark:text-purple-400">{data.next_speaker}</span>
      </div>
      <p className="text-foreground/80 leading-relaxed">{data.is_request_satisfied_reason}</p>
      {data.instruction_or_question && (
        <div className="rounded bg-muted/40 px-2 py-1 text-foreground/80 leading-relaxed">
          {data.instruction_or_question}
        </div>
      )}
    </div>
  );
}
