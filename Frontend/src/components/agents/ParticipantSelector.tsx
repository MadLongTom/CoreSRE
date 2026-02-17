import { useState, useEffect, useMemo } from "react";
import { X, Search, Loader2 } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Label } from "@/components/ui/label";
import { getAgents } from "@/lib/api/agents";
import type { AgentSummary } from "@/types/agent";

interface ParticipantSelectorProps {
  value: string[];
  onChange: (ids: string[]) => void;
  /** Agent ID to exclude from the list (e.g., current agent being edited) */
  excludeId?: string;
  label?: string;
}

/**
 * Multi-select Agent list for Team participants.
 * Async loads agents, excludes self and Team-type agents (no nesting).
 */
export default function ParticipantSelector({
  value,
  onChange,
  excludeId,
  label = "参与者 Agent",
}: ParticipantSelectorProps) {
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await getAgents();
        if (!cancelled && result.success && result.data) {
          setAgents(result.data);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  // Filter out Team-type agents and self
  const available = useMemo(() => {
    return agents.filter(
      (a) =>
        a.agentType !== "Team" &&
        a.id !== excludeId &&
        (search === "" ||
          a.name.toLowerCase().includes(search.toLowerCase()))
    );
  }, [agents, excludeId, search]);

  const selected = useMemo(
    () => agents.filter((a) => value.includes(a.id)),
    [agents, value]
  );

  const toggleAgent = (id: string) => {
    if (value.includes(id)) {
      onChange(value.filter((v) => v !== id));
    } else {
      onChange([...value, id]);
    }
  };

  return (
    <div className="space-y-3">
      <Label>{label}</Label>

      {/* Selected agents */}
      {selected.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {selected.map((a) => (
            <Badge key={a.id} variant="secondary" className="gap-1 pr-1">
              <span className="text-xs text-muted-foreground">{a.agentType}</span>
              <span>{a.name}</span>
              <button
                type="button"
                onClick={() => toggleAgent(a.id)}
                className="ml-1 rounded-full p-0.5 hover:bg-muted"
              >
                <X className="h-3 w-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}

      {/* Search + list */}
      <div className="relative">
        <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          placeholder="搜索 Agent..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-9"
        />
      </div>

      {loading ? (
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" /> 加载中...
        </div>
      ) : (
        <div className="max-h-48 overflow-y-auto rounded-md border">
          {available.length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">无可用 Agent</p>
          ) : (
            available.map((a) => (
              <label
                key={a.id}
                className="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-muted/50"
              >
                <input
                  type="checkbox"
                  checked={value.includes(a.id)}
                  onChange={() => toggleAgent(a.id)}
                  className="rounded"
                />
                <span className="flex-1 text-sm">{a.name}</span>
                <Badge variant="outline" className="text-xs">
                  {a.agentType}
                </Badge>
              </label>
            ))
          )}
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        已选择 {value.length} 个 Agent（Team 类型不可嵌套选择）
      </p>
    </div>
  );
}
