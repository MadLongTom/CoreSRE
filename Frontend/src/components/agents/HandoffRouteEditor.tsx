import { useMemo } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { HandoffTarget, AgentSummary } from "@/types/agent";

interface HandoffRouteEditorProps {
  /** Map of sourceAgentId → HandoffTarget[] */
  value: Record<string, HandoffTarget[]>;
  onChange: (routes: Record<string, HandoffTarget[]>) => void;
  /** IDs of participant agents (only these can be sources/targets) */
  participantIds: string[];
  /** Agent summaries for name display */
  agents: AgentSummary[];
}

/**
 * Visual editor for Handoff routes: source → target with optional reason.
 */
export default function HandoffRouteEditor({
  value,
  onChange,
  participantIds,
  agents,
}: HandoffRouteEditorProps) {
  const agentMap = useMemo(() => {
    const map = new Map<string, string>();
    agents.forEach((a) => map.set(a.id, a.name));
    return map;
  }, [agents]);

  const getAgentName = (id: string) => agentMap.get(id) ?? id.slice(0, 8);

  const addRoute = (sourceId: string) => {
    const existing = value[sourceId] ?? [];
    // Find first participant not already a target and not the source itself
    const availableTarget = participantIds.find(
      (p) => p !== sourceId && !existing.some((t) => t.targetAgentId === p)
    );
    if (!availableTarget) return;

    const updated = {
      ...value,
      [sourceId]: [...existing, { targetAgentId: availableTarget, reason: null }],
    };
    onChange(updated);
  };

  const removeRoute = (sourceId: string, targetIdx: number) => {
    const existing = [...(value[sourceId] ?? [])];
    existing.splice(targetIdx, 1);
    const updated = { ...value };
    if (existing.length === 0) {
      delete updated[sourceId];
    } else {
      updated[sourceId] = existing;
    }
    onChange(updated);
  };

  const updateTarget = (
    sourceId: string,
    targetIdx: number,
    field: "targetAgentId" | "reason",
    newValue: string
  ) => {
    const existing = [...(value[sourceId] ?? [])];
    existing[targetIdx] = {
      ...existing[targetIdx],
      [field]: field === "reason" ? (newValue || null) : newValue,
    };
    onChange({ ...value, [sourceId]: existing });
  };

  // Sources that don't have routes yet
  const sourcesWithoutRoutes = participantIds.filter(
    (id) => !value[id] || value[id].length === 0
  );

  return (
    <div className="space-y-4">
      <Label>交接路由</Label>

      {/* Existing routes grouped by source */}
      {Object.entries(value).map(([sourceId, targets]) => (
        <div key={sourceId} className="rounded-md border p-3 space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">
              {getAgentName(sourceId)} 可交接到：
            </span>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => addRoute(sourceId)}
              disabled={
                targets.length >= participantIds.length - 1 // Can't route to self
              }
            >
              <Plus className="h-3 w-3 mr-1" /> 添加目标
            </Button>
          </div>

          {targets.map((target, idx) => (
            <div key={idx} className="flex items-center gap-2">
              <span className="text-sm text-muted-foreground">→</span>
              <Select
                value={target.targetAgentId}
                onValueChange={(v) => updateTarget(sourceId, idx, "targetAgentId", v)}
              >
                <SelectTrigger className="w-40">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {participantIds
                    .filter((p) => p !== sourceId)
                    .map((p) => (
                      <SelectItem key={p} value={p}>
                        {getAgentName(p)}
                      </SelectItem>
                    ))}
                </SelectContent>
              </Select>
              <Input
                placeholder="交接原因（可选）"
                value={target.reason ?? ""}
                onChange={(e) => updateTarget(sourceId, idx, "reason", e.target.value)}
                className="flex-1"
              />
              <Button
                type="button"
                variant="ghost"
                size="icon"
                onClick={() => removeRoute(sourceId, idx)}
              >
                <Trash2 className="h-4 w-4 text-destructive" />
              </Button>
            </div>
          ))}
        </div>
      ))}

      {/* Add new source */}
      {sourcesWithoutRoutes.length > 0 && (
        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">为 Agent 添加路由：</span>
          {sourcesWithoutRoutes.map((id) => (
            <Button
              key={id}
              type="button"
              variant="outline"
              size="sm"
              onClick={() => addRoute(id)}
            >
              <Plus className="h-3 w-3 mr-1" /> {getAgentName(id)}
            </Button>
          ))}
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        定义每个 Agent 可以交接到哪些 Agent，以及交接原因
      </p>
    </div>
  );
}
