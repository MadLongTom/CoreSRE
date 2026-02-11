import { useState, useEffect, useCallback } from "react";
import { useParams, Link } from "react-router";
import { ArrowLeft, Loader2, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { PageHeader } from "@/components/layout/PageHeader";
import { ExecutionStatusBadge } from "@/components/workflows/ExecutionStatusBadge";
import { DagExecutionViewer } from "@/components/workflows/DagExecutionViewer";
import { NodeExecutionTimeline } from "@/components/workflows/NodeExecutionTimeline";
import { getWorkflowById, getWorkflowExecutionById } from "@/lib/api/workflows";
import type { WorkflowDetail, WorkflowExecutionDetail } from "@/types/workflow";

export default function WorkflowExecutionDetailPage() {
  const { id: workflowId, execId } = useParams<{
    id: string;
    execId: string;
  }>();

  const [workflow, setWorkflow] = useState<WorkflowDetail | null>(null);
  const [execution, setExecution] = useState<WorkflowExecutionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    if (!workflowId || !execId) return;
    setLoading(true);
    setError(null);
    try {
      const [wfRes, execRes] = await Promise.all([
        getWorkflowById(workflowId),
        getWorkflowExecutionById(workflowId, execId),
      ]);

      if (wfRes.success && wfRes.data) setWorkflow(wfRes.data);
      if (execRes.success && execRes.data) setExecution(execRes.data);

      if (!wfRes.success || !execRes.success) {
        setError("加载失败");
      }
    } catch {
      setError("加载失败，请重试");
    } finally {
      setLoading(false);
    }
  }, [workflowId, execId]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  // Auto-refresh for running executions
  useEffect(() => {
    if (!execution) return;
    if (execution.status === "Running" || execution.status === "Pending") {
      const timer = setInterval(fetchData, 3000);
      return () => clearInterval(timer);
    }
  }, [execution, fetchData]);

  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error || !workflow || !execution) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-sm text-muted-foreground">{error ?? "未找到执行记录"}</p>
        <Button variant="outline" size="sm" onClick={fetchData}>
          重试
        </Button>
        <Button variant="ghost" size="sm" asChild>
          <Link to={`/workflows/${workflowId}`}>返回 Workflow</Link>
        </Button>
      </div>
    );
  }

  const isRunning =
    execution.status === "Running" || execution.status === "Pending";

  // Selected node detail
  const selectedExec = selectedNodeId
    ? execution.nodeExecutions?.find((ne) => ne.nodeId === selectedNodeId)
    : null;

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={`执行详情`}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to={`/workflows/${workflowId}`}>
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
        }
        actions={
          isRunning ? (
            <Button variant="ghost" size="sm" onClick={fetchData}>
              <RefreshCw className="mr-1 h-4 w-4 animate-spin" /> 刷新中…
            </Button>
          ) : null
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Execution metadata */}
        <div className="flex flex-wrap items-center gap-4 text-sm">
          <ExecutionStatusBadge status={execution.status} />
          <span className="text-muted-foreground">
            开始 {new Date(execution.startedAt).toLocaleString()}
          </span>
          {execution.completedAt && (
            <span className="text-muted-foreground">
              结束 {new Date(execution.completedAt).toLocaleString()}
            </span>
          )}
          <span className="font-mono text-xs text-muted-foreground">
            ID: {execution.id.slice(0, 8)}…
          </span>
        </div>

        {/* Error message */}
        {execution.errorMessage && (
          <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
            <p className="text-sm text-destructive">{execution.errorMessage}</p>
          </div>
        )}

        {/* DAG with execution overlay + timeline side by side */}
        <div className="grid gap-6 lg:grid-cols-[1fr_300px]">
          <div>
            <Label className="mb-2 block">执行 DAG</Label>
            <DagExecutionViewer
              nodes={workflow.graph.nodes}
              edges={workflow.graph.edges}
              nodeExecutions={execution.nodeExecutions ?? []}
            />
          </div>

          <div>
            <Label className="mb-2 block">节点执行顺序</Label>
            <NodeExecutionTimeline
              nodeExecutions={execution.nodeExecutions ?? []}
              onSelect={setSelectedNodeId}
              selectedNodeId={selectedNodeId}
            />
          </div>
        </div>

        {/* Selected node detail */}
        {selectedExec && (
          <div className="rounded-md border p-4 space-y-3 max-w-2xl">
            <h3 className="font-medium text-sm">
              节点: {selectedExec.nodeId}
            </h3>
            <div className="grid gap-2 text-sm">
              <div className="flex gap-2">
                <span className="text-muted-foreground w-16">状态</span>
                <ExecutionStatusBadge status={selectedExec.status as any} />
              </div>
              {selectedExec.startedAt && (
                <div className="flex gap-2">
                  <span className="text-muted-foreground w-16">开始</span>
                  <span>{new Date(selectedExec.startedAt).toLocaleString()}</span>
                </div>
              )}
              {selectedExec.completedAt && (
                <div className="flex gap-2">
                  <span className="text-muted-foreground w-16">结束</span>
                  <span>
                    {new Date(selectedExec.completedAt).toLocaleString()}
                  </span>
                </div>
              )}
              {selectedExec.errorMessage && (
                <div className="flex gap-2">
                  <span className="text-muted-foreground w-16">错误</span>
                  <span className="text-destructive">{selectedExec.errorMessage}</span>
                </div>
              )}
              {selectedExec.output && (
                <div>
                  <span className="text-muted-foreground block mb-1">输出</span>
                  <pre className="rounded bg-muted p-2 text-xs overflow-x-auto max-h-48">
                    {typeof selectedExec.output === "string"
                      ? selectedExec.output
                      : JSON.stringify(selectedExec.output, null, 2)}
                  </pre>
                </div>
              )}
            </div>
          </div>
        )}

        {/* Input / Output data */}
        <div className="grid gap-6 md:grid-cols-2 max-w-4xl">
          {execution.input && (
            <div>
              <Label className="mb-2 block">输入数据</Label>
              <pre className="rounded-md border bg-muted p-3 text-xs overflow-x-auto max-h-64">
                {JSON.stringify(execution.input, null, 2)}
              </pre>
            </div>
          )}
          {execution.output && (
            <div>
              <Label className="mb-2 block">输出数据</Label>
              <pre className="rounded-md border bg-muted p-3 text-xs overflow-x-auto max-h-64">
                {JSON.stringify(execution.output, null, 2)}
              </pre>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
