import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router";
import { Plus, RefreshCw, Loader2, Trash2, GitBranch, Eye } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { WorkflowStatusBadge } from "@/components/workflows/WorkflowStatusBadge";
import { DeleteWorkflowDialog } from "@/components/workflows/DeleteWorkflowDialog";
import { PageHeader } from "@/components/layout/PageHeader";
import { getWorkflows, type ApiError } from "@/lib/api/workflows";
import type { WorkflowSummary } from "@/types/workflow";
import { WORKFLOW_STATUSES } from "@/types/workflow";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

function truncate(text: string | null, maxLen: number): string {
  if (!text) return "";
  return text.length > maxLen ? text.slice(0, maxLen) + "…" : text;
}

export default function WorkflowListPage() {
  const [workflows, setWorkflows] = useState<WorkflowSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>("all");

  // Delete dialog state
  const [deleteTarget, setDeleteTarget] = useState<{
    id: string;
    name: string;
  } | null>(null);

  const fetchWorkflows = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const filterValue = statusFilter === "all" ? undefined : statusFilter;
      const result = await getWorkflows(filterValue);
      if (result.success && result.data) {
        setWorkflows(result.data);
      } else {
        setError(result.message ?? "Failed to load workflows");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "Failed to load workflows");
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    fetchWorkflows();
  }, [fetchWorkflows]);

  const handleDeleted = () => {
    setDeleteTarget(null);
    fetchWorkflows();
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="工作流管理"
        actions={
          <Button asChild size="sm">
            <Link to="/workflows/new">
              <Plus className="mr-2 h-4 w-4" />
              新建 Workflow
            </Link>
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Filter bar */}
        <div className="flex items-center gap-4">
          <Select value={statusFilter} onValueChange={setStatusFilter}>
            <SelectTrigger className="w-48">
              <SelectValue placeholder="筛选状态" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部状态</SelectItem>
              {WORKFLOW_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>
                  {s}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Content */}
        {loading ? (
          <div className="flex items-center justify-center py-20">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center gap-4 py-20">
            <p className="text-destructive">{error}</p>
            <Button variant="outline" onClick={fetchWorkflows}>
              <RefreshCw className="mr-2 h-4 w-4" />
              重试
            </Button>
          </div>
        ) : workflows.length === 0 ? (
          <div className="flex flex-col items-center gap-4 py-20 text-muted-foreground">
            <GitBranch className="h-12 w-12" />
            <p>暂无工作流</p>
            <Button asChild variant="outline">
              <Link to="/workflows/new">
                <Plus className="mr-2 h-4 w-4" />
                新建 Workflow
              </Link>
            </Button>
          </div>
        ) : (
          <div className="rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>描述</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead className="w-28">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {workflows.map((wf) => (
                  <TableRow key={wf.id}>
                    <TableCell>
                      <Link
                        to={`/workflows/${wf.id}`}
                        className="font-medium text-primary hover:underline"
                      >
                        {wf.name}
                      </Link>
                    </TableCell>
                    <TableCell className="text-muted-foreground text-sm max-w-xs">
                      {truncate(wf.description, 50)}
                    </TableCell>
                    <TableCell>
                      <WorkflowStatusBadge status={wf.status} />
                    </TableCell>
                    <TableCell className="text-muted-foreground text-sm">
                      {formatDate(wf.createdAt)}
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-1">
                        <Button variant="ghost" size="icon" asChild>
                          <Link to={`/workflows/${wf.id}`}>
                            <Eye className="h-4 w-4" />
                          </Link>
                        </Button>
                        <TooltipProvider>
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <span>
                                <Button
                                  variant="ghost"
                                  size="icon"
                                  disabled={wf.status === "Published"}
                                  onClick={() =>
                                    setDeleteTarget({
                                      id: wf.id,
                                      name: wf.name,
                                    })
                                  }
                                >
                                  <Trash2 className="h-4 w-4 text-destructive" />
                                </Button>
                              </span>
                            </TooltipTrigger>
                            {wf.status === "Published" && (
                              <TooltipContent>
                                Published 工作流需先取消发布才能删除
                              </TooltipContent>
                            )}
                          </Tooltip>
                        </TooltipProvider>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}

        {/* Delete dialog */}
        {deleteTarget && (
          <DeleteWorkflowDialog
            workflowId={deleteTarget.id}
            workflowName={deleteTarget.name}
            open={!!deleteTarget}
            onOpenChange={(open) => {
              if (!open) setDeleteTarget(null);
            }}
            onDeleted={handleDeleted}
          />
        )}
      </div>
    </div>
  );
}
