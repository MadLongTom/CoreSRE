import { useState, useEffect, useCallback, useRef } from "react";
import { useParams, Link, useNavigate } from "react-router";
import {
  ArrowLeft,
  Edit,
  Eye,
  Loader2,
  Save,
  X,
  Upload,
  ArchiveRestore,
  Play,
  Trash2,
  History,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { PageHeader } from "@/components/layout/PageHeader";
import { DagViewer } from "@/components/workflows/DagViewer";
import { DagEditor } from "@/components/workflows/DagEditor";
import { WorkflowStatusBadge } from "@/components/workflows/WorkflowStatusBadge";
import { DeleteWorkflowDialog } from "@/components/workflows/DeleteWorkflowDialog";
import { ExecuteWorkflowDialog } from "@/components/workflows/ExecuteWorkflowDialog";
import { ExecutionHistoryTable } from "@/components/workflows/ExecutionHistoryTable";
import {
  getWorkflowById,
  updateWorkflow,
  ApiError,
} from "@/lib/api/workflows";
import { useUnsavedChangesGuard } from "@/hooks/useUnsavedChangesGuard";
import type { WorkflowDetail, WorkflowGraph, WorkflowStatus } from "@/types/workflow";

export default function WorkflowDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [workflow, setWorkflow] = useState<WorkflowDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Edit mode
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState("");
  const [editDesc, setEditDesc] = useState("");
  const editGraphRef = useRef<WorkflowGraph>({ nodes: [], edges: [] });
  const [saving, setSaving] = useState(false);
  const [saveErrors, setSaveErrors] = useState<string[]>([]);

  // Dialogs
  const [showDelete, setShowDelete] = useState(false);
  const [showExecute, setShowExecute] = useState(false);
  const [showHistory, setShowHistory] = useState(false);

  // Unsaved-changes guard when in edit mode
  useUnsavedChangesGuard(editing);

  const fetchWorkflow = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const result = await getWorkflowById(id);
      if (result.success && result.data) {
        setWorkflow(result.data);
      } else {
        setError(result.message ?? "加载失败");
      }
    } catch {
      setError("加载失败，请重试");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    fetchWorkflow();
  }, [fetchWorkflow]);

  const enterEdit = () => {
    if (!workflow) return;
    setEditName(workflow.name);
    setEditDesc(workflow.description ?? "");
    editGraphRef.current = workflow.graph;
    setEditing(true);
    setSaveErrors([]);
  };

  const cancelEdit = () => {
    setEditing(false);
    setSaveErrors([]);
  };

  const handleSave = async () => {
    if (!workflow) return;
    const validationErrors: string[] = [];
    if (!editName.trim()) validationErrors.push("名称为必填项");
    if (editName.length > 200) validationErrors.push("名称不能超过 200 个字符");
    if (editDesc.length > 2000) validationErrors.push("描述不能超过 2000 个字符");
    const graph = editGraphRef.current;
    if (graph.nodes.length < 2) validationErrors.push("DAG 至少需要 2 个节点");
    if (graph.edges.length < 1) validationErrors.push("DAG 至少需要 1 条边");
    if (validationErrors.length > 0) {
      setSaveErrors(validationErrors);
      return;
    }

    setSaving(true);
    setSaveErrors([]);
    try {
      const result = await updateWorkflow(workflow.id, {
        name: editName.trim(),
        description: editDesc.trim() || null,
        graph,
      });
      if (result.success && result.data) {
        setWorkflow(result.data);
        setEditing(false);
      } else {
        setSaveErrors([result.message ?? "保存失败"]);
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setSaveErrors(err.errors ?? [err.message]);
      } else {
        setSaveErrors(["保存失败，请重试"]);
      }
    } finally {
      setSaving(false);
    }
  };

  const handlePublishToggle = async () => {
    if (!workflow) return;
    const newStatus: WorkflowStatus =
      workflow.status === "Draft" ? "Published" : "Draft";
    setSaving(true);
    try {
      const result = await updateWorkflow(workflow.id, {
        name: workflow.name,
        description: workflow.description,
        graph: workflow.graph,
        status: newStatus,
      });
      if (result.success && result.data) {
        setWorkflow(result.data);
      }
    } catch {
      // silent — user can retry
    } finally {
      setSaving(false);
    }
  };

  const handleDeleted = () => {
    navigate("/workflows");
  };

  // Loading state
  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  // Error / not found
  if (error || !workflow) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-sm text-muted-foreground">{error ?? "未找到该 Workflow"}</p>
        <Button variant="outline" size="sm" onClick={fetchWorkflow}>
          重试
        </Button>
        <Button variant="ghost" size="sm" asChild>
          <Link to="/workflows">返回列表</Link>
        </Button>
      </div>
    );
  }

  const isDraft = workflow.status === "Draft";

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={editing ? "编辑 Workflow" : workflow.name}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/workflows">
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
        }
        actions={
          <div className="flex items-center gap-2">
            {editing ? (
              <>
                <Button variant="ghost" size="sm" onClick={cancelEdit}>
                  <X className="mr-1 h-4 w-4" /> 取消
                </Button>
                <Button size="sm" onClick={handleSave} disabled={saving}>
                  {saving ? (
                    <Loader2 className="mr-1 h-4 w-4 animate-spin" />
                  ) : (
                    <Save className="mr-1 h-4 w-4" />
                  )}
                  保存
                </Button>
              </>
            ) : (
              <>
                {isDraft && (
                  <Button variant="outline" size="sm" onClick={enterEdit}>
                    <Edit className="mr-1 h-4 w-4" /> 编辑
                  </Button>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handlePublishToggle}
                  disabled={saving}
                >
                  {isDraft ? (
                    <>
                      <Upload className="mr-1 h-4 w-4" /> 发布
                    </>
                  ) : (
                    <>
                      <ArchiveRestore className="mr-1 h-4 w-4" /> 取消发布
                    </>
                  )}
                </Button>
                {workflow.status === "Published" && (
                  <Button size="sm" onClick={() => setShowExecute(true)}>
                    <Play className="mr-1 h-4 w-4" /> 执行
                  </Button>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setShowHistory(!showHistory)}
                >
                  <History className="mr-1 h-4 w-4" /> 历史
                </Button>
                {isDraft && (
                  <Button
                    variant="destructive"
                    size="sm"
                    onClick={() => setShowDelete(true)}
                  >
                    <Trash2 className="mr-1 h-4 w-4" /> 删除
                  </Button>
                )}
              </>
            )}
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Save errors */}
        {saveErrors.length > 0 && (
          <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
            <ul className="list-disc list-inside space-y-1">
              {saveErrors.map((e, i) => (
                <li key={i} className="text-sm text-destructive">
                  {e}
                </li>
              ))}
            </ul>
          </div>
        )}

        {editing ? (
          /* ---- Edit mode ---- */
          <>
            <div className="grid gap-4 max-w-2xl">
              <div className="space-y-2">
                <Label htmlFor="edit-name">名称 *</Label>
                <Input
                  id="edit-name"
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  maxLength={200}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="edit-desc">描述</Label>
                <Textarea
                  id="edit-desc"
                  value={editDesc}
                  onChange={(e) => setEditDesc(e.target.value)}
                  maxLength={2000}
                  rows={3}
                />
              </div>
            </div>
            <div>
              <Label className="mb-2 block">DAG 编辑器</Label>
              <DagEditor
                initialGraph={workflow.graph}
                onChange={(g) => {
                  editGraphRef.current = g;
                }}
              />
            </div>
          </>
        ) : (
          /* ---- View mode ---- */
          <>
            {/* Metadata */}
            <div className="flex flex-wrap items-center gap-4 text-sm">
              <WorkflowStatusBadge status={workflow.status} />
              <span className="text-muted-foreground">
                创建于 {new Date(workflow.createdAt).toLocaleString()}
              </span>
              {workflow.updatedAt && (
                <span className="text-muted-foreground">
                  更新于 {new Date(workflow.updatedAt).toLocaleString()}
                </span>
              )}
            </div>
            {workflow.description && (
              <p className="text-sm text-muted-foreground max-w-2xl">
                {workflow.description}
              </p>
            )}

            {/* DAG Viewer */}
            <div>
              <div className="flex items-center gap-2 mb-2">
                <Eye className="h-4 w-4 text-muted-foreground" />
                <Label>工作流图</Label>
              </div>
              <DagViewer
                nodes={workflow.graph.nodes}
                edges={workflow.graph.edges}
              />
            </div>

            {/* Execution history (collapsible) */}
            {showHistory && id && (
              <div>
                <Label className="mb-2 block">执行历史</Label>
                <ExecutionHistoryTable workflowId={id} />
              </div>
            )}
          </>
        )}
      </div>

      {/* Dialogs */}
      {showDelete && (
        <DeleteWorkflowDialog
          workflowId={workflow.id}
          workflowName={workflow.name}
          open={showDelete}
          onOpenChange={setShowDelete}
          onDeleted={handleDeleted}
        />
      )}
      {showExecute && id && (
        <ExecuteWorkflowDialog
          workflowId={id}
          open={showExecute}
          onOpenChange={setShowExecute}
          onExecuted={(execId) => navigate(`/workflows/${id}/executions/${execId}`)}
        />
      )}
    </div>
  );
}
