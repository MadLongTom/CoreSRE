import { useState, useEffect } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Separator } from "@/components/ui/separator";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import ParticipantSelector from "@/components/agents/ParticipantSelector";
import HandoffRouteEditor from "@/components/agents/HandoffRouteEditor";
import ProviderModelSelect from "@/components/agents/ProviderModelSelect";
import { getAgents } from "@/lib/api/agents";
import type {
  TeamConfig,
  TeamMode,
  HandoffTarget,
  AgentSummary,
} from "@/types/agent";
import {
  TEAM_MODES,
  TEAM_MODE_LABELS,
  TEAM_MODE_DESCRIPTIONS,
} from "@/types/agent";

interface TeamConfigFormProps {
  value: TeamConfig;
  onChange: (config: TeamConfig) => void;
  /** Current agent ID for exclusion in participant selector */
  excludeAgentId?: string;
}

const DEFAULT_TEAM_CONFIG: TeamConfig = {
  mode: "RoundRobin",
  participantIds: [],
  maxIterations: 40,
  allowRepeatedSpeaker: true,
  maxStalls: 3,
};

/**
 * Team configuration form — conditionally renders mode-specific fields.
 */
export default function TeamConfigForm({
  value,
  onChange,
  excludeAgentId,
}: TeamConfigFormProps) {
  const [agents, setAgents] = useState<AgentSummary[]>([]);

  useEffect(() => {
    (async () => {
      const result = await getAgents();
      if (result.success && result.data) setAgents(result.data);
    })();
  }, []);

  const update = (partial: Partial<TeamConfig>) => {
    onChange({ ...value, ...partial });
  };

  const mode = value.mode as TeamMode;

  const showHandoffs = mode === "Handoffs";
  const showSelector = mode === "Selector";
  const showMagneticOne = mode === "MagneticOne";
  const showConcurrent = mode === "Concurrent";

  return (
    <div className="space-y-6">
      {/* Mode Selection */}
      <div className="space-y-2">
        <Label>编排模式</Label>
        <div className="grid gap-3 md:grid-cols-3">
          {TEAM_MODES.map((m) => (
            <Card
              key={m}
              className={`cursor-pointer transition-shadow hover:shadow-md ${
                mode === m ? "border-primary ring-1 ring-primary" : ""
              }`}
              onClick={() => update({ mode: m })}
            >
              <CardHeader className="p-3">
                <CardTitle className="text-sm">{TEAM_MODE_LABELS[m]}</CardTitle>
                <CardDescription className="text-xs">
                  {TEAM_MODE_DESCRIPTIONS[m]}
                </CardDescription>
              </CardHeader>
            </Card>
          ))}
        </div>
      </div>

      <Separator />

      {/* Common: Participants */}
      <ParticipantSelector
        value={value.participantIds}
        onChange={(ids) => update({ participantIds: ids })}
        excludeId={excludeAgentId}
      />

      {/* Common: MaxIterations */}
      <div className="space-y-2">
        <Label htmlFor="maxIterations">最大轮次</Label>
        <Input
          id="maxIterations"
          type="number"
          min={1}
          value={value.maxIterations}
          onChange={(e) =>
            update({ maxIterations: Math.max(1, parseInt(e.target.value) || 1) })
          }
          className="w-32"
        />
        <p className="text-xs text-muted-foreground">
          Agent 协作的最大轮次数，防止无限循环
        </p>
      </div>

      {/* ── Handoffs Mode ──────────────────────────────── */}
      {showHandoffs && (
        <>
          <Separator />
          <div className="space-y-4">
            <h4 className="font-medium text-sm">Handoffs 配置</h4>

            {/* Initial Agent */}
            <div className="space-y-2">
              <Label>初始 Agent</Label>
              <Select
                value={value.initialAgentId ?? ""}
                onValueChange={(v) => update({ initialAgentId: v })}
              >
                <SelectTrigger className="w-64">
                  <SelectValue placeholder="选择初始 Agent" />
                </SelectTrigger>
                <SelectContent>
                  {value.participantIds.map((id) => {
                    const agent = agents.find((a) => a.id === id);
                    return (
                      <SelectItem key={id} value={id}>
                        {agent?.name ?? id.slice(0, 8)}
                      </SelectItem>
                    );
                  })}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                对话开始时首先响应的 Agent
              </p>
            </div>

            {/* Handoff Routes */}
            <HandoffRouteEditor
              value={value.handoffRoutes ?? {}}
              onChange={(routes) => update({ handoffRoutes: routes })}
              participantIds={value.participantIds}
              agents={agents}
            />
          </div>
        </>
      )}

      {/* ── Selector Mode ──────────────────────────────── */}
      {showSelector && (
        <>
          <Separator />
          <div className="space-y-4">
            <h4 className="font-medium text-sm">Selector LLM 配置</h4>

            <ProviderModelSelect
              providerId={value.selectorProviderId ?? null}
              modelId={value.selectorModelId ?? null}
              onProviderChange={(id) => update({ selectorProviderId: id })}
              onModelChange={(id) => update({ selectorModelId: id })}
            />

            <div className="space-y-2">
              <Label htmlFor="selectorPrompt">选择器提示词（可选）</Label>
              <Textarea
                id="selectorPrompt"
                value={value.selectorPrompt ?? ""}
                onChange={(e) =>
                  update({ selectorPrompt: e.target.value || null })
                }
                placeholder="自定义 LLM 选择下一个发言者的提示词..."
                rows={3}
              />
            </div>

            <div className="flex items-center space-x-2">
              <Switch
                id="allowRepeatedSpeaker"
                checked={value.allowRepeatedSpeaker}
                onCheckedChange={(v) => update({ allowRepeatedSpeaker: v })}
              />
              <Label htmlFor="allowRepeatedSpeaker">允许同一 Agent 连续发言</Label>
            </div>
          </div>
        </>
      )}

      {/* ── MagneticOne Mode ──────────────────────────── */}
      {showMagneticOne && (
        <>
          <Separator />
          <div className="space-y-4">
            <h4 className="font-medium text-sm">MagneticOne 编排器配置</h4>

            <ProviderModelSelect
              providerId={value.orchestratorProviderId ?? null}
              modelId={value.orchestratorModelId ?? null}
              onProviderChange={(id) => update({ orchestratorProviderId: id })}
              onModelChange={(id) => update({ orchestratorModelId: id })}
            />

            <div className="space-y-2">
              <Label htmlFor="maxStalls">最大停滞次数</Label>
              <Input
                id="maxStalls"
                type="number"
                min={1}
                value={value.maxStalls}
                onChange={(e) =>
                  update({ maxStalls: Math.max(1, parseInt(e.target.value) || 1) })
                }
                className="w-32"
              />
              <p className="text-xs text-muted-foreground">
                连续无进展的最大次数，超过后强制终止
              </p>
            </div>

            <div className="space-y-2">
              <Label htmlFor="finalAnswerPrompt">最终答案提示词（可选）</Label>
              <Textarea
                id="finalAnswerPrompt"
                value={value.finalAnswerPrompt ?? ""}
                onChange={(e) =>
                  update({ finalAnswerPrompt: e.target.value || null })
                }
                placeholder="自定义最终答案生成的提示词..."
                rows={3}
              />
            </div>
          </div>
        </>
      )}

      {/* ── Concurrent Mode ──────────────────────────── */}
      {showConcurrent && (
        <>
          <Separator />
          <div className="space-y-4">
            <h4 className="font-medium text-sm">并发配置</h4>
            <div className="space-y-2">
              <Label htmlFor="aggregationStrategy">聚合策略（可选）</Label>
              <Input
                id="aggregationStrategy"
                value={value.aggregationStrategy ?? ""}
                onChange={(e) =>
                  update({ aggregationStrategy: e.target.value || null })
                }
                placeholder="如 Merge、Vote 等"
              />
              <p className="text-xs text-muted-foreground">
                定义如何聚合多个 Agent 的并发输出
              </p>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export { DEFAULT_TEAM_CONFIG };
