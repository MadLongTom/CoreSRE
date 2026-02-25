import { useCallback, useMemo } from "react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { AgentSummary } from "@/types/agent";

interface AgentSelectorProps {
  agents: AgentSummary[];
  selectedAgentId: string | null;
  onSelectAgent: (agentId: string) => void;
  disabled?: boolean;
  loading?: boolean;
}

/**
 * Agent 选择器 — 下拉选择已注册的 ChatClient、A2A 或 Team 类型 Agent。
 * 第一条消息发送后锁定（disabled=true）。
 */
export function AgentSelector({
  agents,
  selectedAgentId,
  onSelectAgent,
  disabled = false,
  loading = false,
}: AgentSelectorProps) {
  // 展示 ChatClient、A2A 和 Team 类型的 Agent
  const chatAgents = useMemo(
    () => agents.filter((a) => a.agentType === "ChatClient" || a.agentType === "A2A" || a.agentType === "Team"),
    [agents],
  );

  const handleChange = useCallback(
    (value: string) => {
      onSelectAgent(value);
    },
    [onSelectAgent],
  );

  if (chatAgents.length === 0 && !loading) {
    return (
      <div className="flex items-center rounded-md border border-dashed px-3 py-2 text-sm text-muted-foreground">
        请先注册 Agent
      </div>
    );
  }

  return (
    <Select
      value={selectedAgentId ?? undefined}
      onValueChange={handleChange}
      disabled={disabled || loading}
    >
      <SelectTrigger className="w-60">
        <SelectValue placeholder={loading ? "加载中…" : "选择 Agent"} />
      </SelectTrigger>
      <SelectContent>
        {chatAgents.map((agent) => (
          <SelectItem key={agent.id} value={agent.id}>
            <span className="flex items-center gap-1.5">
              {agent.name}
              {agent.agentType === "A2A" && (
                <span className="rounded bg-blue-100 px-1 py-0.5 text-[10px] font-medium text-blue-700 dark:bg-blue-900 dark:text-blue-300">
                  A2A
                </span>
              )}
              {agent.agentType === "Team" && (
                <span className="rounded bg-purple-100 px-1 py-0.5 text-[10px] font-medium text-purple-700 dark:bg-purple-900 dark:text-purple-300">
                  Team
                </span>
              )}
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
