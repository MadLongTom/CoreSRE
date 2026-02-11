import { useState, useEffect, useCallback } from "react";
import { Link } from "react-router";
import { Loader2, Eye, RefreshCw } from "lucide-react";
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
import { ExecutionStatusBadge } from "@/components/workflows/ExecutionStatusBadge";
import { getWorkflowExecutions } from "@/lib/api/workflows";
import type { WorkflowExecutionSummary, ExecutionStatus } from "@/types/workflow";
import { EXECUTION_STATUSES } from "@/types/workflow";

interface ExecutionHistoryTableProps {
  workflowId: string;
}

export function ExecutionHistoryTable({ workflowId }: ExecutionHistoryTableProps) {
  const [executions, setExecutions] = useState<WorkflowExecutionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<ExecutionStatus | "All">("All");

  const fetchData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const filter = statusFilter === "All" ? undefined : statusFilter;
      const result = await getWorkflowExecutions(workflowId, filter);
      if (result.success && result.data) {
        setExecutions(result.data);
      } else {
        setError(result.message ?? "加载失败");
      }
    } catch {
      setError("加载失败，请重试");
    } finally {
      setLoading(false);
    }
  }, [workflowId, statusFilter]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-4">
        <Select
          value={statusFilter}
          onValueChange={(v) => setStatusFilter(v as ExecutionStatus | "All")}
        >
          <SelectTrigger className="w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="All">全部状态</SelectItem>
            {EXECUTION_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>
                {s}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button variant="ghost" size="icon" onClick={fetchData} disabled={loading}>
          <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
        </Button>
      </div>

      {loading ? (
        <div className="flex justify-center py-8">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : error ? (
        <p className="text-sm text-destructive">{error}</p>
      ) : executions.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4">暂无执行记录</p>
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>执行 ID</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>开始时间</TableHead>
                <TableHead>结束时间</TableHead>
                <TableHead className="w-16" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {executions.map((exec) => (
                <TableRow key={exec.id}>
                  <TableCell className="font-mono text-xs">
                    {exec.id.slice(0, 8)}…
                  </TableCell>
                  <TableCell>
                    <ExecutionStatusBadge status={exec.status} />
                  </TableCell>
                  <TableCell className="text-sm">
                    {new Date(exec.startedAt).toLocaleString()}
                  </TableCell>
                  <TableCell className="text-sm">
                    {exec.completedAt
                      ? new Date(exec.completedAt).toLocaleString()
                      : "—"}
                  </TableCell>
                  <TableCell>
                    <Button variant="ghost" size="icon" asChild>
                      <Link
                        to={`/workflows/${workflowId}/executions/${exec.id}`}
                      >
                        <Eye className="h-4 w-4" />
                      </Link>
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
