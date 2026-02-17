import { useEffect, useMemo, useState } from "react";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { ChevronDown, ChevronRight, FileJson, Terminal, Sparkles } from "lucide-react";
import ProviderModelSelect from "@/components/agents/ProviderModelSelect";
import ToolRefsPicker from "@/components/agents/ToolRefsPicker";
import SkillRefsPicker from "@/components/agents/SkillRefsPicker";
import { getAvailableFunctions } from "@/lib/api/tools";
import { getSandboxes, getSandboxById } from "@/lib/api/sandboxes";
import { getSkills } from "@/lib/api/skills";
import type { SandboxInstance } from "@/types/sandbox";
import type { BindableTool } from "@/types/tool";
import type { LlmConfig } from "@/types/agent";

const EXAMPLE_SCHEMA = JSON.stringify(
  {
    type: "object",
    properties: {
      answer: { type: "string", description: "The answer to the question" },
      confidence: { type: "number", description: "Confidence score 0-1" },
      sources: {
        type: "array",
        items: { type: "string" },
        description: "Source references",
      },
    },
    required: ["answer", "confidence"],
  },
  null,
  2,
);

/** Try parsing JSON; returns null if valid, error message if invalid */
function validateJsonSchema(text: string): string | null {
  if (!text.trim()) return null;
  try {
    const parsed = JSON.parse(text);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      return "Schema 必须是一个 JSON 对象";
    }
    return null;
  } catch (e) {
    return `JSON 语法错误: ${(e as SyntaxError).message}`;
  }
}

interface LlmConfigSectionProps {
  config: LlmConfig;
  editing?: boolean;
  onChange?: (config: LlmConfig) => void;
}

export default function LlmConfigSection({
  config,
  editing = false,
  onChange,
}: LlmConfigSectionProps) {
  const update = (partial: Partial<LlmConfig>) =>
    onChange?.({ ...config, ...partial });

  // Resolve tool names for view mode
  const [resolvedTools, setResolvedTools] = useState<Map<string, BindableTool>>(new Map());
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [historyMemoryOpen, setHistoryMemoryOpen] = useState(false);

  // Load available persistent sandbox instances for the picker
  const [sandboxInstances, setSandboxInstances] = useState<SandboxInstance[]>([]);
  useEffect(() => {
    if (!editing || !config.enableSandbox) return;
    let cancelled = false;
    getSandboxes({ pageSize: 100 })
      .then((result) => {
        if (!cancelled && result.data) {
          setSandboxInstances(result.data.items);
        }
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [editing, config.enableSandbox]);

  // Schema validation (memoised to avoid re-parsing on every render)
  const schemaError = useMemo(
    () => validateJsonSchema(config.responseFormatSchema ?? ""),
    [config.responseFormatSchema],
  );

  useEffect(() => {
    if (editing || config.toolRefs.length === 0) return;
    let cancelled = false;
    getAvailableFunctions({ status: "all" })
      .then((result) => {
        if (!cancelled && result.data) {
          const map = new Map<string, BindableTool>();
          for (const t of result.data) map.set(t.id, t);
          setResolvedTools(map);
        }
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [editing, config.toolRefs]);

  // Resolve sandbox instance name for view mode
  const [resolvedSandboxName, setResolvedSandboxName] = useState<string | null>(null);
  useEffect(() => {
    if (editing || !config.sandboxInstanceId) return;
    let cancelled = false;
    getSandboxById(config.sandboxInstanceId)
      .then((result) => {
        if (!cancelled && result.data) setResolvedSandboxName(result.data.name);
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [editing, config.sandboxInstanceId]);

  // Resolve skill names for view mode
  const [resolvedSkills, setResolvedSkills] = useState<Map<string, string>>(new Map());
  useEffect(() => {
    if (editing || !config.skillRefs?.length) return;
    let cancelled = false;
    getSkills({ pageSize: 200 })
      .then((result) => {
        if (!cancelled && result.data) {
          const map = new Map<string, string>();
          for (const s of result.data.items) map.set(s.id, s.name);
          setResolvedSkills(map);
        }
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [editing, config.skillRefs]);

  // Check if any advanced option has a value
  const hasAdvancedValues =
    config.temperature != null ||
    config.maxOutputTokens != null ||
    config.topP != null ||
    config.topK != null ||
    config.frequencyPenalty != null ||
    config.presencePenalty != null ||
    config.seed != null ||
    (config.stopSequences && config.stopSequences.length > 0) ||
    (config.responseFormat != null && config.responseFormat !== "") ||
    (config.toolMode != null && config.toolMode !== "") ||
    config.allowMultipleToolCalls != null;

  // Check if sandbox is enabled
  const hasSandboxValue = config.enableSandbox != null;

  // Check if any history/memory option has a non-default value
  const hasHistoryMemoryValues =
    config.enableChatHistory != null ||
    config.maxHistoryMessages != null ||
    config.enableSemanticMemory != null ||
    config.embeddingProviderId != null ||
    config.embeddingModelId != null ||
    config.embeddingDimensions != null ||
    config.memorySearchMode != null ||
    config.memoryMaxResults != null ||
    config.memoryMinRelevanceScore != null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>LLM 配置</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {editing ? (
          <>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-2 sm:col-span-2">
                <ProviderModelSelect
                  providerId={config.providerId ?? null}
                  modelId={config.modelId}
                  onProviderChange={(pid) =>
                    update({ providerId: pid ?? undefined, modelId: "" })
                  }
                  onModelChange={(mid) => update({ modelId: mid })}
                />
              </div>
              <div className="space-y-2 sm:col-span-2">
                <Label htmlFor="edit-instructions">Instructions</Label>
                <Textarea
                  id="edit-instructions"
                  value={config.instructions ?? ""}
                  onChange={(e) => update({ instructions: e.target.value })}
                  rows={4}
                />
              </div>
              <div className="space-y-2 sm:col-span-2">
                <Label>Tool Refs</Label>
                <ToolRefsPicker
                  value={config.toolRefs}
                  onChange={(ids) => update({ toolRefs: ids })}
                />
              </div>
              <div className="sm:col-span-2 flex items-center gap-3 rounded-md border p-3 bg-muted/30">
                <Terminal className="h-5 w-5 text-muted-foreground shrink-0" />
                <div className="flex-1 space-y-2">
                  <div className="flex items-center gap-2">
                    <Switch
                      id="edit-enableSandbox"
                      checked={config.enableSandbox ?? false}
                      onCheckedChange={(checked) =>
                        update({
                          enableSandbox: checked ? true : null,
                          sandboxType: checked ? (config.sandboxType ?? "CodeBox") : null,
                          sandboxImage: checked ? config.sandboxImage : null,
                          sandboxCpus: checked ? config.sandboxCpus : null,
                          sandboxMemoryMib: checked ? config.sandboxMemoryMib : null,
                          sandboxK8sNamespace: checked ? config.sandboxK8sNamespace : null,
                          sandboxMode: checked ? (config.sandboxMode ?? "Ephemeral") : null,
                          sandboxInstanceId: checked ? config.sandboxInstanceId : null,
                        })
                      }
                    />
                    <Label htmlFor="edit-enableSandbox" className="font-medium">启用沙盒环境（Kubernetes Pod）</Label>
                  </div>
                  {config.enableSandbox && (
                    <div className="space-y-2 pl-1">
                      <div className="flex items-center gap-2">
                        <Label htmlFor="edit-sandboxMode" className="text-xs text-muted-foreground whitespace-nowrap">沙盒模式</Label>
                        <Select
                          value={config.sandboxMode ?? "Ephemeral"}
                          onValueChange={(v) =>
                            update({
                              sandboxMode: v,
                              sandboxInstanceId: v === "Persistent" ? config.sandboxInstanceId : null,
                            })
                          }
                        >
                          <SelectTrigger id="edit-sandboxMode" className="h-8 w-[200px] text-xs">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="Ephemeral">临时沙箱 — 每次对话新建 Pod</SelectItem>
                            <SelectItem value="Persistent">持久化沙箱 — 绑定已有实例</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      {config.sandboxMode === "Persistent" && (
                        <div className="flex items-center gap-2">
                          <Label htmlFor="edit-sandboxInstanceId" className="text-xs text-muted-foreground whitespace-nowrap">沙箱实例</Label>
                          <Select
                            value={config.sandboxInstanceId ?? ""}
                            onValueChange={(v) => update({ sandboxInstanceId: v || null })}
                          >
                            <SelectTrigger id="edit-sandboxInstanceId" className="h-8 w-[320px] text-xs">
                              <SelectValue placeholder="选择持久化沙箱实例…" />
                            </SelectTrigger>
                            <SelectContent>
                              {sandboxInstances.filter(s => s.status === "Running" || s.status === "Stopped").length === 0 && (
                                <SelectItem value="" disabled>暂无可用沙箱实例，请先在沙箱管理页创建</SelectItem>
                              )}
                              {sandboxInstances
                                .filter(s => s.status === "Running" || s.status === "Stopped")
                                .map((s) => (
                                  <SelectItem key={s.id} value={s.id}>
                                    {s.name} — {s.status} ({s.sandboxType}, {s.image})
                                  </SelectItem>
                                ))}
                            </SelectContent>
                          </Select>
                        </div>
                      )}
                      <div className="flex items-center gap-2">
                        <Label htmlFor="edit-sandboxType" className="text-xs text-muted-foreground whitespace-nowrap">沙盒类型</Label>
                        <Select
                          value={config.sandboxType ?? "CodeBox"}
                          onValueChange={(v) => update({ sandboxType: v })}
                        >
                          <SelectTrigger id="edit-sandboxType" className="h-8 w-[200px] text-xs">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="SimpleBox">SimpleBox — 基础文件与命令行</SelectItem>
                            <SelectItem value="CodeBox">CodeBox — 代码执行 + 包安装</SelectItem>
                            <SelectItem value="InteractiveBox">InteractiveBox — 交互式终端</SelectItem>
                            <SelectItem value="BrowserBox">BrowserBox — 浏览器自动化</SelectItem>
                            <SelectItem value="ComputerBox">ComputerBox — 桌面自动化</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="flex items-center gap-3">
                        <div className="flex items-center gap-1">
                          <Label htmlFor="edit-sandboxImage" className="text-xs text-muted-foreground whitespace-nowrap">镜像</Label>
                          <Input
                            id="edit-sandboxImage"
                            value={config.sandboxImage ?? ""}
                            onChange={(e) => update({ sandboxImage: e.target.value || null })}
                            placeholder="自动（根据沙盒类型）"
                            className="h-8 w-[220px] text-xs"
                          />
                        </div>
                        <div className="flex items-center gap-1">
                          <Label htmlFor="edit-sandboxCpus" className="text-xs text-muted-foreground whitespace-nowrap">CPU</Label>
                          <Input
                            id="edit-sandboxCpus"
                            type="number"
                            min={0}
                            value={config.sandboxCpus ?? ""}
                            onChange={(e) => update({ sandboxCpus: e.target.value ? Number(e.target.value) : null })}
                            placeholder="默认"
                            className="h-8 w-[80px] text-xs"
                          />
                        </div>
                        <div className="flex items-center gap-1">
                          <Label htmlFor="edit-sandboxMemory" className="text-xs text-muted-foreground whitespace-nowrap">内存 MiB</Label>
                          <Input
                            id="edit-sandboxMemory"
                            type="number"
                            min={0}
                            value={config.sandboxMemoryMib ?? ""}
                            onChange={(e) => update({ sandboxMemoryMib: e.target.value ? Number(e.target.value) : null })}
                            placeholder="默认"
                            className="h-8 w-[80px] text-xs"
                          />
                        </div>
                      </div>
                      <div className="flex items-center gap-3">
                        <div className="flex items-center gap-1">
                          <Label htmlFor="edit-sandboxK8sNamespace" className="text-xs text-muted-foreground whitespace-nowrap">K8s Namespace</Label>
                          <Input
                            id="edit-sandboxK8sNamespace"
                            value={config.sandboxK8sNamespace ?? ""}
                            onChange={(e) => update({ sandboxK8sNamespace: e.target.value || null })}
                            placeholder="coresre-sandbox"
                            className="h-8 w-[220px] text-xs"
                          />
                        </div>
                      </div>
                    </div>
                  )}
                  <p className="text-xs text-muted-foreground">
                    在 Kubernetes Pod 容器中提供资源隔离的命令行、文件读写和代码执行能力。临时沙箱每次对话新建 Pod；持久化沙箱绑定已有实例，Pod 文件可持久保存。
                  </p>
                </div>
              </div>

              {/* Skill Bindings */}
              <div className="sm:col-span-2 space-y-2">
                <Label className="flex items-center gap-1.5">
                  <Sparkles className="h-4 w-4" /> 绑定 Skill
                </Label>
                <p className="text-xs text-muted-foreground mb-2">
                  选择要绑定到此 Agent 的 Skill。绑定后 Skill 摘要将注入 SystemPrompt，LLM 可通过 read_skill 工具按需加载完整内容。
                </p>
                <SkillRefsPicker
                  value={config.skillRefs ?? []}
                  onChange={(ids) => update({ skillRefs: ids })}
                  sandboxEnabled={config.enableSandbox ?? false}
                />
              </div>
            </div>

            {/* Advanced ChatOptions */}
            <Collapsible open={advancedOpen} onOpenChange={setAdvancedOpen}>
              <CollapsibleTrigger className="flex items-center gap-1 text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                {advancedOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                高级推理参数
                {hasAdvancedValues && <Badge variant="secondary" className="ml-2 text-xs">已配置</Badge>}
              </CollapsibleTrigger>
              <CollapsibleContent className="mt-3 space-y-4 rounded-md border p-4">
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label htmlFor="edit-temperature">Temperature</Label>
                    <Input
                      id="edit-temperature"
                      type="number"
                      step="0.1"
                      min="0"
                      max="2"
                      placeholder="默认"
                      value={config.temperature ?? ""}
                      onChange={(e) =>
                        update({ temperature: e.target.value ? parseFloat(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-maxOutputTokens">Max Output Tokens</Label>
                    <Input
                      id="edit-maxOutputTokens"
                      type="number"
                      min="1"
                      placeholder="默认"
                      value={config.maxOutputTokens ?? ""}
                      onChange={(e) =>
                        update({ maxOutputTokens: e.target.value ? parseInt(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-topP">Top P</Label>
                    <Input
                      id="edit-topP"
                      type="number"
                      step="0.05"
                      min="0"
                      max="1"
                      placeholder="默认"
                      value={config.topP ?? ""}
                      onChange={(e) =>
                        update({ topP: e.target.value ? parseFloat(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-topK">Top K</Label>
                    <Input
                      id="edit-topK"
                      type="number"
                      min="1"
                      placeholder="默认"
                      value={config.topK ?? ""}
                      onChange={(e) =>
                        update({ topK: e.target.value ? parseInt(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-frequencyPenalty">Frequency Penalty</Label>
                    <Input
                      id="edit-frequencyPenalty"
                      type="number"
                      step="0.1"
                      min="-2"
                      max="2"
                      placeholder="默认"
                      value={config.frequencyPenalty ?? ""}
                      onChange={(e) =>
                        update({ frequencyPenalty: e.target.value ? parseFloat(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-presencePenalty">Presence Penalty</Label>
                    <Input
                      id="edit-presencePenalty"
                      type="number"
                      step="0.1"
                      min="-2"
                      max="2"
                      placeholder="默认"
                      value={config.presencePenalty ?? ""}
                      onChange={(e) =>
                        update({ presencePenalty: e.target.value ? parseFloat(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-seed">Seed</Label>
                    <Input
                      id="edit-seed"
                      type="number"
                      placeholder="默认"
                      value={config.seed ?? ""}
                      onChange={(e) =>
                        update({ seed: e.target.value ? parseInt(e.target.value) : null })
                      }
                    />
                  </div>
                  <div className="space-y-2 col-span-2">
                    <Label htmlFor="edit-responseFormat">Response Format</Label>
                    <Select
                      value={config.responseFormat ?? ""}
                      onValueChange={(v) => update({ responseFormat: v || null, responseFormatSchema: v === "Json" ? config.responseFormatSchema : null })}
                    >
                      <SelectTrigger id="edit-responseFormat">
                        <SelectValue placeholder="默认" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="Text">Text</SelectItem>
                        <SelectItem value="Json">Json</SelectItem>
                      </SelectContent>
                    </Select>
                    {config.responseFormat === "Json" && (
                      <div className="mt-2 space-y-1">
                        <div className="flex items-center justify-between">
                          <Label htmlFor="edit-responseFormatSchema" className="text-xs text-muted-foreground">
                            JSON Schema（可选，约束输出结构）
                          </Label>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="h-6 px-2 text-xs"
                            onClick={() => update({ responseFormatSchema: EXAMPLE_SCHEMA })}
                          >
                            <FileJson className="mr-1 h-3 w-3" />
                            插入示例
                          </Button>
                        </div>
                        <Textarea
                          id="edit-responseFormatSchema"
                          value={config.responseFormatSchema ?? ""}
                          onChange={(e) => update({ responseFormatSchema: e.target.value || null })}
                          placeholder={EXAMPLE_SCHEMA}
                          rows={8}
                          className={`font-mono text-xs ${schemaError ? "border-destructive focus-visible:ring-destructive" : ""}`}
                        />
                        {schemaError && (
                          <p className="text-xs text-destructive">{schemaError}</p>
                        )}
                      </div>
                    )}
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="edit-toolMode">Tool Mode</Label>
                    <Select
                      value={config.toolMode ?? ""}
                      onValueChange={(v) => update({ toolMode: v || null })}
                    >
                      <SelectTrigger id="edit-toolMode">
                        <SelectValue placeholder="默认 (Auto)" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="Auto">Auto</SelectItem>
                        <SelectItem value="Required">Required</SelectItem>
                        <SelectItem value="None">None</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="flex items-center gap-2 pt-6">
                    <Switch
                      id="edit-allowMultipleToolCalls"
                      checked={config.allowMultipleToolCalls ?? false}
                      onCheckedChange={(checked) =>
                        update({ allowMultipleToolCalls: checked ? true : null })
                      }
                    />
                    <Label htmlFor="edit-allowMultipleToolCalls">允许多工具调用</Label>
                  </div>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="edit-stopSequences">Stop Sequences</Label>
                  <Input
                    id="edit-stopSequences"
                    placeholder="以逗号分隔，如: END,STOP"
                    value={config.stopSequences?.join(", ") ?? ""}
                    onChange={(e) =>
                      update({
                        stopSequences: e.target.value
                          ? e.target.value.split(",").map((s) => s.trim()).filter(Boolean)
                          : null,
                      })
                    }
                  />
                </div>
              </CollapsibleContent>
            </Collapsible>

            {/* History & Memory Configuration */}
            <Collapsible open={historyMemoryOpen} onOpenChange={setHistoryMemoryOpen}>
              <CollapsibleTrigger className="flex items-center gap-1 text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                {historyMemoryOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                历史与记忆
                {hasHistoryMemoryValues && <Badge variant="secondary" className="ml-2 text-xs">已配置</Badge>}
              </CollapsibleTrigger>
              <CollapsibleContent className="mt-3 space-y-4 rounded-md border p-4">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div className="flex items-center gap-2">
                    <Switch
                      id="edit-enableChatHistory"
                      checked={config.enableChatHistory ?? true}
                      onCheckedChange={(checked) =>
                        update({ enableChatHistory: checked ? null : false })
                      }
                    />
                    <Label htmlFor="edit-enableChatHistory">启用对话历史</Label>
                  </div>
                  {(config.enableChatHistory ?? true) && (
                    <div className="space-y-2">
                      <Label htmlFor="edit-maxHistoryMessages">最大历史消息数</Label>
                      <Input
                        id="edit-maxHistoryMessages"
                        type="number"
                        min="1"
                        placeholder="默认 (50)"
                        value={config.maxHistoryMessages ?? ""}
                        onChange={(e) =>
                          update({ maxHistoryMessages: e.target.value ? parseInt(e.target.value) : null })
                        }
                      />
                    </div>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    id="edit-enableSemanticMemory"
                    checked={config.enableSemanticMemory ?? false}
                    onCheckedChange={(checked) =>
                      update({ enableSemanticMemory: checked ? true : null })
                    }
                  />
                  <Label htmlFor="edit-enableSemanticMemory">启用语义记忆</Label>
                </div>
                {config.enableSemanticMemory && (
                  <>
                    <div className="rounded-md border p-3 space-y-3 bg-muted/30">
                      <Label className="text-xs font-medium text-muted-foreground">Embedding 模型配置</Label>
                      <ProviderModelSelect
                        providerId={config.embeddingProviderId ?? config.providerId ?? null}
                        modelId={config.embeddingModelId ?? ""}
                        onProviderChange={(pid) =>
                          update({ embeddingProviderId: pid ?? null, embeddingModelId: "" })
                        }
                        onModelChange={(mid) => update({ embeddingModelId: mid })}
                      />
                      <div className="space-y-2">
                        <Label htmlFor="edit-embeddingDimensions">向量维度</Label>
                        <Input
                          id="edit-embeddingDimensions"
                          type="number"
                          min="1"
                          placeholder="默认 (1536)"
                          value={config.embeddingDimensions ?? ""}
                          onChange={(e) =>
                            update({ embeddingDimensions: e.target.value ? parseInt(e.target.value) : null })
                          }
                        />
                      </div>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <div className="space-y-2">
                        <Label htmlFor="edit-memorySearchMode">记忆检索模式</Label>
                        <Select
                          value={config.memorySearchMode ?? "BeforeAIInvoke"}
                          onValueChange={(v) => update({ memorySearchMode: v || null })}
                        >
                          <SelectTrigger id="edit-memorySearchMode">
                            <SelectValue placeholder="BeforeAIInvoke" />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="BeforeAIInvoke">BeforeAIInvoke</SelectItem>
                            <SelectItem value="OnDemandFunctionCalling">OnDemandFunctionCalling</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="edit-memoryMaxResults">最大检索结果数</Label>
                        <Input
                          id="edit-memoryMaxResults"
                          type="number"
                          min="1"
                          placeholder="默认 (5)"
                          value={config.memoryMaxResults ?? ""}
                          onChange={(e) =>
                            update({ memoryMaxResults: e.target.value ? parseInt(e.target.value) : null })
                          }
                        />
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="edit-memoryMinRelevanceScore">最低相关性分数</Label>
                        <Input
                          id="edit-memoryMinRelevanceScore"
                          type="number"
                          min="0"
                          max="1"
                          step="0.05"
                          placeholder="默认 (0 = 不过滤)"
                          value={config.memoryMinRelevanceScore ?? ""}
                          onChange={(e) =>
                            update({ memoryMinRelevanceScore: e.target.value ? parseFloat(e.target.value) : null })
                          }
                        />
                        <p className="text-muted-foreground text-xs">
                          0~1，越高越严格。低于此分数的记忆不会注入上下文。
                        </p>
                      </div>
                    </div>
                  </>
                )}
              </CollapsibleContent>
            </Collapsible>
          </>
        ) : (
          <>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-3">
              {config.providerName && (
                <div className="space-y-1">
                  <Label className="text-muted-foreground text-xs">Provider</Label>
                  <p className="text-sm font-medium">{config.providerName}</p>
                </div>
              )}
              <div className="space-y-1">
                <Label className="text-muted-foreground text-xs">Model ID</Label>
                <p className="text-sm font-medium">{config.modelId || "—"}</p>
              </div>
              <div className="space-y-1 sm:col-span-2">
                <Label className="text-muted-foreground text-xs">
                  Instructions
                </Label>
                <p className="text-sm whitespace-pre-wrap">
                  {config.instructions || "—"}
                </p>
              </div>
              <div className="space-y-1 sm:col-span-2">
                <Label className="text-muted-foreground text-xs">
                  Tool Refs
                </Label>
              {config.toolRefs.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {config.toolRefs.map((ref, i) => {
                    const tool = resolvedTools.get(ref);
                    return (
                      <Badge key={i} variant="secondary" className="text-xs">
                        {tool ? tool.name : ref}
                        {tool?.toolType === "McpTool" && tool.parentName && (
                          <span className="ml-1 text-muted-foreground">
                            ({tool.parentName})
                          </span>
                        )}
                      </Badge>
                    );
                  })}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">无</p>
              )}
              </div>
              {hasSandboxValue && (
                <div className="space-y-1 sm:col-span-2">
                  <Label className="text-muted-foreground text-xs">沙盒环境（Kubernetes Pod）</Label>
                  <div className="flex items-center gap-2">
                    <Terminal className="h-4 w-4 text-muted-foreground" />
                    <p className="text-sm">
                      {config.enableSandbox
                        ? `已启用 — ${config.sandboxMode === "Persistent" ? "持久化" : "临时"}沙箱 ${config.sandboxType ?? "CodeBox"}${config.sandboxImage ? ` (${config.sandboxImage})` : ""}${config.sandboxCpus ? ` ${config.sandboxCpus}C` : ""}${config.sandboxMemoryMib ? ` ${config.sandboxMemoryMib}MiB` : ""}${config.sandboxK8sNamespace ? ` ns:${config.sandboxK8sNamespace}` : ""}${config.sandboxMode === "Persistent" && config.sandboxInstanceId ? ` → ${resolvedSandboxName ?? config.sandboxInstanceId.slice(0, 8) + "…"}` : ""}`
                        : "未启用"}
                    </p>
                  </div>
                </div>
              )}
              {(config.skillRefs?.length ?? 0) > 0 && (
                <div className="space-y-1 sm:col-span-2">
                  <Label className="text-muted-foreground text-xs flex items-center gap-1">
                    <Sparkles className="h-3.5 w-3.5" /> 绑定 Skill
                  </Label>
                  <div className="flex flex-wrap gap-1">
                    {config.skillRefs!.map((ref, i) => (
                      <Badge key={i} variant="secondary" className="text-xs">
                        {resolvedSkills.get(ref) ?? ref.slice(0, 8) + "…"}
                      </Badge>
                    ))}
                  </div>
                </div>
              )}
            </div>

            {/* Advanced ChatOptions — view mode */}
            {hasAdvancedValues && (
              <Collapsible open={advancedOpen} onOpenChange={setAdvancedOpen}>
                <CollapsibleTrigger className="flex items-center gap-1 text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                  {advancedOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                  高级推理参数
                </CollapsibleTrigger>
                <CollapsibleContent className="mt-3 space-y-2 rounded-md border p-4">
                  <div className="grid grid-cols-2 gap-x-6 gap-y-2">
                    {config.temperature != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Temperature</Label>
                        <p className="text-sm">{config.temperature}</p>
                      </div>
                    )}
                    {config.maxOutputTokens != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Max Output Tokens</Label>
                        <p className="text-sm">{config.maxOutputTokens}</p>
                      </div>
                    )}
                    {config.topP != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Top P</Label>
                        <p className="text-sm">{config.topP}</p>
                      </div>
                    )}
                    {config.topK != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Top K</Label>
                        <p className="text-sm">{config.topK}</p>
                      </div>
                    )}
                    {config.frequencyPenalty != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Frequency Penalty</Label>
                        <p className="text-sm">{config.frequencyPenalty}</p>
                      </div>
                    )}
                    {config.presencePenalty != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Presence Penalty</Label>
                        <p className="text-sm">{config.presencePenalty}</p>
                      </div>
                    )}
                    {config.seed != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Seed</Label>
                        <p className="text-sm">{config.seed}</p>
                      </div>
                    )}
                    {config.responseFormat && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Response Format</Label>
                        <p className="text-sm">{config.responseFormat}</p>
                        {config.responseFormat === "Json" && config.responseFormatSchema && (
                          <pre className="mt-1 rounded bg-muted p-2 text-xs font-mono whitespace-pre-wrap break-all">
                            {config.responseFormatSchema}
                          </pre>
                        )}
                      </div>
                    )}
                    {config.toolMode && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Tool Mode</Label>
                        <p className="text-sm">{config.toolMode}</p>
                      </div>
                    )}
                    {config.allowMultipleToolCalls != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">允许多工具调用</Label>
                        <p className="text-sm">{config.allowMultipleToolCalls ? "是" : "否"}</p>
                      </div>
                    )}
                  </div>
                  {config.stopSequences && config.stopSequences.length > 0 && (
                    <div className="space-y-0.5">
                      <Label className="text-muted-foreground text-xs">Stop Sequences</Label>
                      <div className="flex flex-wrap gap-1">
                        {config.stopSequences.map((seq, i) => (
                          <Badge key={i} variant="outline" className="text-xs">{seq}</Badge>
                        ))}
                      </div>
                    </div>
                  )}
                </CollapsibleContent>
              </Collapsible>
            )}

            {/* History & Memory — view mode */}
            {hasHistoryMemoryValues && (
              <Collapsible open={historyMemoryOpen} onOpenChange={setHistoryMemoryOpen}>
                <CollapsibleTrigger className="flex items-center gap-1 text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                  {historyMemoryOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                  历史与记忆
                </CollapsibleTrigger>
                <CollapsibleContent className="mt-3 space-y-2 rounded-md border p-4">
                  <div className="grid grid-cols-2 gap-x-6 gap-y-2">
                    {config.enableChatHistory != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">对话历史</Label>
                        <p className="text-sm">{config.enableChatHistory ? "启用" : "禁用"}</p>
                      </div>
                    )}
                    {config.maxHistoryMessages != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">最大历史消息数</Label>
                        <p className="text-sm">{config.maxHistoryMessages}</p>
                      </div>
                    )}
                    {config.enableSemanticMemory != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">语义记忆</Label>
                        <p className="text-sm">{config.enableSemanticMemory ? "启用" : "禁用"}</p>
                      </div>
                    )}
                    {config.enableSemanticMemory && config.embeddingProviderName && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Embedding Provider</Label>
                        <p className="text-sm">{config.embeddingProviderName}</p>
                      </div>
                    )}
                    {config.enableSemanticMemory && config.embeddingModelId && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">Embedding Model</Label>
                        <p className="text-sm">{config.embeddingModelId}</p>
                      </div>
                    )}
                    {config.enableSemanticMemory && config.embeddingDimensions != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">向量维度</Label>
                        <p className="text-sm">{config.embeddingDimensions}</p>
                      </div>
                    )}
                    {config.memorySearchMode != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">检索模式</Label>
                        <p className="text-sm">{config.memorySearchMode}</p>
                      </div>
                    )}
                    {config.memoryMaxResults != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">最大检索结果数</Label>
                        <p className="text-sm">{config.memoryMaxResults}</p>
                      </div>
                    )}
                    {config.memoryMinRelevanceScore != null && (
                      <div className="space-y-0.5">
                        <Label className="text-muted-foreground text-xs">最低相关性分数</Label>
                        <p className="text-sm">{config.memoryMinRelevanceScore}</p>
                      </div>
                    )}
                  </div>
                </CollapsibleContent>
              </Collapsible>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
