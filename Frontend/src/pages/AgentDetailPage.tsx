import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useBlocker } from "react-router";
import { ArrowLeft, Loader2, Pencil, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { AgentTypeBadge } from "@/components/agents/AgentTypeBadge";
import { AgentStatusBadge } from "@/components/agents/AgentStatusBadge";
import AgentCardSection from "@/components/agents/AgentCardSection";
import LlmConfigSection from "@/components/agents/LlmConfigSection";
import BoundToolsSection from "@/components/agents/BoundToolsSection";
import TeamConfigForm from "@/components/agents/TeamConfigForm";
import { DEFAULT_TEAM_CONFIG } from "@/components/agents/TeamConfigForm";
import { DeleteAgentDialog } from "@/components/agents/DeleteAgentDialog";
import { Combobox, type ComboboxOption } from "@/components/ui/combobox";
import { PageHeader } from "@/components/layout/PageHeader";
import { getAgentById, updateAgent, ApiError } from "@/lib/api/agents";
import { getWorkflows } from "@/lib/api/workflows";
import type {
  AgentRegistration,
  AgentCard,
  LlmConfig,
  TeamConfig,
  UpdateAgentRequest,
} from "@/types/agent";
import { TEAM_MODE_LABELS } from "@/types/agent";
import type { TeamMode } from "@/types/agent";
import type { WorkflowSummary } from "@/types/workflow";

export default function AgentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [agent, setAgent] = useState<AgentRegistration | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Edit state
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveErrors, setSaveErrors] = useState<string[]>([]);
  const [dirty, setDirty] = useState(false);

  // Editable field state
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editEndpoint, setEditEndpoint] = useState("");
  const [editWorkflowRef, setEditWorkflowRef] = useState("");
  const [editAgentCard, setEditAgentCard] = useState<AgentCard | null>(null);
  const [editLlmConfig, setEditLlmConfig] = useState<LlmConfig | null>(null);
  const [editTeamConfig, setEditTeamConfig] = useState<TeamConfig>(DEFAULT_TEAM_CONFIG);

  // Delete dialog
  const [showDelete, setShowDelete] = useState(false);

  // Workflow options for combobox
  const [workflows, setWorkflows] = useState<WorkflowSummary[]>([]);

  useEffect(() => {
    getWorkflows()
      .then((result) => {
        if (result.success && result.data) setWorkflows(result.data);
      })
      .catch(() => {});
  }, []);

  const workflowOptions = useMemo<ComboboxOption[]>(
    () =>
      workflows.map((w) => ({
        value: w.id,
        label: w.name,
        description: w.status,
      })),
    [workflows],
  );

  // Block navigation with unsaved changes
  const blocker = useBlocker(dirty && editing);

  // Warn on browser close
  useEffect(() => {
    if (!dirty || !editing) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty, editing]);

  // Handle blocker
  useEffect(() => {
    if (blocker.state === "blocked") {
      const leave = window.confirm("有未保存的更改，确定要离开吗？");
      if (leave) {
        blocker.proceed();
      } else {
        blocker.reset();
      }
    }
  }, [blocker]);

  const fetchAgent = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    setNotFound(false);
    try {
      const result = await getAgentById(id);
      if (result.success && result.data) {
        setAgent(result.data);
      } else {
        setError(result.message ?? "加载失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 404) {
        setNotFound(true);
      } else {
        setError(apiErr.message);
      }
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    fetchAgent();
  }, [fetchAgent]);

  const startEditing = () => {
    if (!agent) return;
    setEditName(agent.name);
    setEditDescription(agent.description ?? "");
    setEditEndpoint(agent.endpoint ?? "");
    setEditWorkflowRef(agent.workflowRef ?? "");
    setEditAgentCard(
      agent.agentCard
        ? JSON.parse(JSON.stringify(agent.agentCard))
        : null,
    );
    setEditLlmConfig(
      agent.llmConfig
        ? JSON.parse(JSON.stringify(agent.llmConfig))
        : null,
    );
    setEditTeamConfig(
      agent.teamConfig
        ? JSON.parse(JSON.stringify(agent.teamConfig))
        : DEFAULT_TEAM_CONFIG,
    );
    setEditing(true);
    setDirty(false);
    setSaveErrors([]);
  };

  const cancelEditing = () => {
    setEditing(false);
    setDirty(false);
    setSaveErrors([]);
  };

  const handleSave = async () => {
    if (!agent || !id) return;

    const validationErrors: string[] = [];
    if (!editName.trim()) validationErrors.push("名称不能为空");
    if (editName.length > 200) validationErrors.push("名称不能超过 200 个字符");
    if (validationErrors.length > 0) {
      setSaveErrors(validationErrors);
      return;
    }

    setSaving(true);
    setSaveErrors([]);

    const request: UpdateAgentRequest = {
      name: editName.trim(),
      description: editDescription.trim() || undefined,
    };

    if (agent.agentType === "A2A") {
      request.endpoint = editEndpoint.trim() || undefined;
      request.agentCard = editAgentCard ?? undefined;
    } else if (agent.agentType === "ChatClient") {
      request.llmConfig = editLlmConfig ?? undefined;
    } else if (agent.agentType === "Workflow") {
      request.workflowRef = editWorkflowRef.trim() || undefined;
    } else if (agent.agentType === "Team") {
      request.teamConfig = editTeamConfig;
    }

    try {
      const result = await updateAgent(id, request);
      if (result.success && result.data) {
        setAgent(result.data);
        setEditing(false);
        setDirty(false);
      } else {
        setSaveErrors(result.errors ?? [result.message ?? "保存失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setSaveErrors(
        apiErr.errors ?? [apiErr.message ?? "保存失败，请重试"],
      );
    } finally {
      setSaving(false);
    }
  };

  // Mark as dirty when any editable field changes
  const markDirty = () => {
    if (!dirty) setDirty(true);
  };

  // ---------- Rendering ----------

  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <XCircle className="h-12 w-12 text-muted-foreground" />
        <h2 className="text-xl font-semibold">Agent 未找到</h2>
        <Button asChild variant="outline">
          <Link to="/agents">返回列表</Link>
        </Button>
      </div>
    );
  }

  if (error || !agent) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-destructive">{error ?? "加载失败"}</p>
        <Button variant="outline" onClick={fetchAgent}>
          重试
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={editing ? "编辑 Agent" : agent.name}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/agents">
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
        }
        actions={
          !editing ? (
            <>
              <AgentTypeBadge type={agent.agentType} />
              <AgentStatusBadge status={agent.status} />
              <Button variant="outline" size="sm" onClick={startEditing}>
                <Pencil className="mr-2 h-4 w-4" />
                编辑
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={() => setShowDelete(true)}
              >
                删除
              </Button>
            </>
          ) : undefined
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
      <div className="space-y-6">

      {/* Save errors */}
      {saveErrors.length > 0 && (
        <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
          <ul className="list-disc pl-5 text-sm text-destructive space-y-1">
            {saveErrors.map((e, i) => (
              <li key={i}>{e}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="grid grid-cols-1 gap-6">
      {/* Basic info — compact overview card */}
      <Card>
        <CardHeader>
          <CardTitle>基本信息</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {editing ? (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="detail-name">名称 *</Label>
                <Input
                  id="detail-name"
                  value={editName}
                  onChange={(e) => {
                    setEditName(e.target.value);
                    markDirty();
                  }}
                  maxLength={200}
                />
              </div>
              <div className="space-y-2">
                <Label className="text-muted-foreground text-xs">
                  Agent 类型（不可修改）
                </Label>
                <p className="text-sm font-medium pt-1">{agent.agentType}</p>
              </div>
              <div className="space-y-2 sm:col-span-2">
                <Label htmlFor="detail-desc">描述</Label>
                <Textarea
                  id="detail-desc"
                  value={editDescription}
                  onChange={(e) => {
                    setEditDescription(e.target.value);
                    markDirty();
                  }}
                  rows={2}
                />
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
              <div>
                <Label className="text-muted-foreground text-xs">ID</Label>
                <p className="font-mono truncate" title={agent.id}>{agent.id}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">类型</Label>
                <p>{agent.agentType}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">创建时间</Label>
                <p>{new Date(agent.createdAt).toLocaleString()}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">更新时间</Label>
                <p>{agent.updatedAt ? new Date(agent.updatedAt).toLocaleString() : "—"}</p>
              </div>
              {agent.description && (
                <div className="col-span-2 sm:col-span-4">
                  <Label className="text-muted-foreground text-xs">描述</Label>
                  <p className="text-sm">{agent.description}</p>
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Type-specific sections — full width */}
      {agent.agentType === "A2A" && (
        <>
          <Card>
            <CardHeader>
              <CardTitle>Endpoint</CardTitle>
            </CardHeader>
            <CardContent>
              {editing ? (
                <Input
                  value={editEndpoint}
                  onChange={(e) => {
                    setEditEndpoint(e.target.value);
                    markDirty();
                  }}
                  placeholder="https://example.com/agent"
                  type="url"
                />
              ) : (
                <p className="text-sm">
                  {agent.endpoint ? (
                    <a
                      href={agent.endpoint}
                      className="text-blue-600 underline"
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      {agent.endpoint}
                    </a>
                  ) : (
                    "—"
                  )}
                </p>
              )}
            </CardContent>
          </Card>

          {(agent.agentCard || editing) && (
            <AgentCardSection
              card={
                editing
                  ? editAgentCard ?? {
                      skills: [],
                      interfaces: [],
                      securitySchemes: [],
                    }
                  : agent.agentCard!
              }
              editing={editing}
              onChange={(card) => {
                setEditAgentCard(card);
                markDirty();
              }}
            />
          )}
        </>
      )}

      {agent.agentType === "ChatClient" && (agent.llmConfig || editing) && (
        <LlmConfigSection
          config={
            editing
              ? editLlmConfig ?? {
                  modelId: "",
                  toolRefs: [],
                }
              : agent.llmConfig!
          }
          editing={editing}
          onChange={(config) => {
            setEditLlmConfig(config);
            markDirty();
          }}
        />
      )}

      {/* Bound tools overview (view mode only) */}
      {agent.agentType === "ChatClient" &&
        !editing &&
        agent.llmConfig &&
        agent.llmConfig.toolRefs.length > 0 && (
          <BoundToolsSection toolRefs={agent.llmConfig.toolRefs} />
        )}

      {agent.agentType === "Workflow" && (
        <Card>
          <CardHeader>
            <CardTitle>Workflow Ref</CardTitle>
          </CardHeader>
          <CardContent>
            {editing ? (
              <Combobox
                options={workflowOptions}
                value={editWorkflowRef}
                onChange={(val) => {
                  setEditWorkflowRef(val);
                  markDirty();
                }}
                placeholder="选择 Workflow"
                searchPlaceholder="搜索 Workflow…"
                emptyText="未找到 Workflow"
              />
            ) : (
              <p className="text-sm font-mono">
                {agent.workflowRef
                  ? workflows.find((w) => w.id === agent.workflowRef)?.name ?? agent.workflowRef
                  : "—"}
              </p>
            )}
          </CardContent>
        </Card>
      )}

      {agent.agentType === "Team" && (
        <Card>
          <CardHeader>
            <CardTitle>Team 配置</CardTitle>
          </CardHeader>
          <CardContent>
            {editing ? (
              <TeamConfigForm
                value={editTeamConfig}
                onChange={(config) => {
                  setEditTeamConfig(config);
                  markDirty();
                }}
                excludeAgentId={agent.id}
              />
            ) : agent.teamConfig ? (
              <div className="space-y-3 text-sm">
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
                  <div>
                    <Label className="text-muted-foreground text-xs">编排模式</Label>
                    <p>{TEAM_MODE_LABELS[agent.teamConfig.mode as TeamMode] ?? agent.teamConfig.mode}</p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">最大轮次</Label>
                    <p>{agent.teamConfig.maxIterations}</p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">参与者数量</Label>
                    <p>{agent.teamConfig.participantIds.length}</p>
                  </div>
                  {agent.teamConfig.mode === "Handoffs" && agent.teamConfig.initialAgentId && (
                    <div>
                      <Label className="text-muted-foreground text-xs">初始 Agent</Label>
                      <p className="font-mono truncate" title={agent.teamConfig.initialAgentId}>
                        {agent.teamConfig.initialAgentId.slice(0, 8)}…
                      </p>
                    </div>
                  )}
                </div>
                {agent.teamConfig.mode === "Selector" && (
                  <div>
                    <Label className="text-muted-foreground text-xs">允许连续发言</Label>
                    <p>{agent.teamConfig.allowRepeatedSpeaker ? "是" : "否"}</p>
                  </div>
                )}
                {agent.teamConfig.mode === "MagneticOne" && (
                  <div>
                    <Label className="text-muted-foreground text-xs">最大停滞次数</Label>
                    <p>{agent.teamConfig.maxStalls}</p>
                  </div>
                )}
                {agent.teamConfig.mode === "Concurrent" && agent.teamConfig.aggregationStrategy && (
                  <div>
                    <Label className="text-muted-foreground text-xs">聚合策略</Label>
                    <p>{agent.teamConfig.aggregationStrategy}</p>
                  </div>
                )}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">— 未配置 —</p>
            )}
          </CardContent>
        </Card>
      )}
      </div>

      {/* Edit actions */}
      {editing && (
        <div className="flex justify-end gap-3">
          <Button
            variant="outline"
            onClick={cancelEditing}
            disabled={saving}
          >
            取消
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            保存
          </Button>
        </div>
      )}

      {/* Delete dialog */}
      {agent && (
        <DeleteAgentDialog
          agentId={agent.id}
          agentName={agent.name}
          open={showDelete}
          onOpenChange={setShowDelete}
          onDeleted={() => navigate("/agents")}
        />
      )}
    </div>
    </div>
    </div>
  );
}
