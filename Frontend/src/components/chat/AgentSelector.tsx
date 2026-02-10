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
 * Agent 选择器 — 下拉选择已注册的 ChatClient 类型 Agent。
 * 第一条消息发送后锁定（disabled=true）。
 */
export function AgentSelector({
  agents,
  selectedAgentId,
  onSelectAgent,
  disabled = false,
  loading = false,
}: AgentSelectorProps) {
  // 只展示 ChatClient 类型的 Agent
  const chatAgents = useMemo(
    () => agents.filter((a) => a.agentType === "ChatClient"),
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
            {agent.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
