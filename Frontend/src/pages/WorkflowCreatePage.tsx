import { useState, useCallback, useRef } from "react";
import { useNavigate, Link } from "react-router";
import { ArrowLeft, Loader2, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { DagEditor } from "@/components/workflows/DagEditor";
import { PageHeader } from "@/components/layout/PageHeader";
import { createWorkflow, ApiError } from "@/lib/api/workflows";
import { useUnsavedChangesGuard } from "@/hooks/useUnsavedChangesGuard";
import type { WorkflowGraph } from "@/types/workflow";

export default function WorkflowCreatePage() {
  const navigate = useNavigate();

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);

  // Track latest graph from DagEditor
  const graphRef = useRef<WorkflowGraph>({ nodes: [], edges: [] });

  // Dirty tracking for unsaved-changes guard
  const isDirty = name.trim().length > 0 || description.trim().length > 0 || graphRef.current.nodes.length > 0;
  useUnsavedChangesGuard(isDirty);

  const handleGraphChange = useCallback((graph: WorkflowGraph) => {
    graphRef.current = graph;
  }, []);

  const handleSave = async () => {
    // Frontend validation
    const validationErrors: string[] = [];
    if (!name.trim()) {
      validationErrors.push("名称为必填项");
    }
    if (name.length > 200) {
      validationErrors.push("名称不能超过 200 个字符");
    }
    if (description.length > 2000) {
      validationErrors.push("描述不能超过 2000 个字符");
    }

    const graph = graphRef.current;
    if (graph.nodes.length < 2) {
      validationErrors.push("DAG 至少需要 2 个节点");
    }
    if (graph.edges.length < 1) {
      validationErrors.push("DAG 至少需要 1 条边");
    }

    if (validationErrors.length > 0) {
      setErrors(validationErrors);
      return;
    }

    setSaving(true);
    setErrors([]);

    try {
      const result = await createWorkflow({
        name: name.trim(),
        description: description.trim() || null,
        graph,
      });

      if (result.success && result.data) {
        navigate(`/workflows/${result.data.id}`);
      } else {
        setErrors([result.message ?? "创建失败"]);
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setErrors(err.errors ?? [err.message]);
      } else {
        setErrors(["创建失败，请重试"]);
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建 Workflow"
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/workflows">
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
        }
        actions={
          <Button size="sm" onClick={handleSave} disabled={saving}>
            {saving ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Save className="mr-2 h-4 w-4" />
            )}
            保存
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Error display */}
        {errors.length > 0 && (
          <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
            <ul className="list-disc list-inside space-y-1">
              {errors.map((e, i) => (
                <li key={i} className="text-sm text-destructive">
                  {e}
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Form fields */}
        <div className="grid gap-4 max-w-2xl">
          <div className="space-y-2">
            <Label htmlFor="name">名称 *</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="输入工作流名称"
              maxLength={200}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="description">描述</Label>
            <Textarea
              id="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="可选描述"
              maxLength={2000}
              rows={3}
            />
          </div>
        </div>

        {/* DAG Editor */}
        <div>
          <Label className="mb-2 block">DAG 编辑器</Label>
          <DagEditor onChange={handleGraphChange} />
        </div>
      </div>
    </div>
  );
}
