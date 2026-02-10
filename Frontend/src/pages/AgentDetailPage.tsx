import { useCallback, useEffect, useState } from "react";
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
import { DeleteAgentDialog } from "@/components/agents/DeleteAgentDialog";
import { getAgentById, updateAgent, ApiError } from "@/lib/api/agents";
import type {
  AgentRegistration,
  AgentCard,
  LlmConfig,
  UpdateAgentRequest,
} from "@/types/agent";

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

  // Delete dialog
  const [showDelete, setShowDelete] = useState(false);

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
      <div className="flex items-center justify-center py-20">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="flex flex-col items-center gap-4 py-20">
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
      <div className="flex flex-col items-center gap-4 py-20">
        <p className="text-destructive">{error ?? "加载失败"}</p>
        <Button variant="outline" onClick={fetchAgent}>
          重试
        </Button>
      </div>
    );
  }

  return (
    <div className="max-w-3xl space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" size="icon" asChild>
            <Link to="/agents">
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
          <h1 className="text-2xl font-bold">
            {editing ? "编辑 Agent" : agent.name}
          </h1>
          <AgentTypeBadge type={agent.agentType} />
          <AgentStatusBadge status={agent.status} />
        </div>
        <div className="flex gap-2">
          {!editing && (
            <>
              <Button variant="outline" onClick={startEditing}>
                <Pencil className="mr-2 h-4 w-4" />
                编辑
              </Button>
              <Button
                variant="destructive"
                onClick={() => setShowDelete(true)}
              >
                删除
              </Button>
            </>
          )}
        </div>
      </div>

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

      {/* Basic info */}
      <Card>
        <CardHeader>
          <CardTitle>基本信息</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {editing ? (
            <>
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
                <Label htmlFor="detail-desc">描述</Label>
                <Textarea
                  id="detail-desc"
                  value={editDescription}
                  onChange={(e) => {
                    setEditDescription(e.target.value);
                    markDirty();
                  }}
                  rows={3}
                />
              </div>
              <div className="space-y-1">
                <Label className="text-muted-foreground text-xs">
                  Agent 类型（不可修改）
                </Label>
                <p className="text-sm font-medium">{agent.agentType}</p>
              </div>
            </>
          ) : (
            <>
              <div className="grid grid-cols-2 gap-4 text-sm">
                <div>
                  <Label className="text-muted-foreground text-xs">ID</Label>
                  <p className="font-mono">{agent.id}</p>
                </div>
                <div>
                  <Label className="text-muted-foreground text-xs">
                    类型
                  </Label>
                  <p>{agent.agentType}</p>
                </div>
                <div>
                  <Label className="text-muted-foreground text-xs">
                    创建时间
                  </Label>
                  <p>{new Date(agent.createdAt).toLocaleString()}</p>
                </div>
                <div>
                  <Label className="text-muted-foreground text-xs">
                    更新时间
                  </Label>
                  <p>
                    {agent.updatedAt
                      ? new Date(agent.updatedAt).toLocaleString()
                      : "—"}
                  </p>
                </div>
              </div>
              {agent.description && (
                <div>
                  <Label className="text-muted-foreground text-xs">
                    描述
                  </Label>
                  <p className="text-sm">{agent.description}</p>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      {/* Type-specific sections */}
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

      {agent.agentType === "Workflow" && (
        <Card>
          <CardHeader>
            <CardTitle>Workflow Ref</CardTitle>
          </CardHeader>
          <CardContent>
            {editing ? (
              <Input
                value={editWorkflowRef}
                onChange={(e) => {
                  setEditWorkflowRef(e.target.value);
                  markDirty();
                }}
                placeholder="Workflow GUID"
              />
            ) : (
              <p className="text-sm font-mono">
                {agent.workflowRef ?? "—"}
              </p>
            )}
          </CardContent>
        </Card>
      )}

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
  );
}
