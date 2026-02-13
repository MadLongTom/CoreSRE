import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router";
import { Loader2, Plus, Container, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import { getSandboxes, startSandbox, stopSandbox, deleteSandbox } from "@/lib/api/sandboxes";
import type { SandboxInstance } from "@/types/sandbox";
import { SANDBOX_STATUSES } from "@/types/sandbox";
import { DeleteSandboxDialog } from "@/components/sandboxes/DeleteSandboxDialog";
import { SandboxStatusBadge } from "@/components/sandboxes/SandboxStatusBadge";

export default function SandboxListPage() {
  const [sandboxes, setSandboxes] = useState<SandboxInstance[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [deleteTarget, setDeleteTarget] = useState<SandboxInstance | null>(null);

  const fetchSandboxes = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getSandboxes({
        status: statusFilter !== "all" ? statusFilter : undefined,
        search: search || undefined,
      });
      if (result.success && result.data) {
        setSandboxes(result.data.items);
        setTotalCount(result.data.totalCount);
      } else {
        setError(result.message ?? "加载沙箱列表失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "加载沙箱列表失败");
    } finally {
      setLoading(false);
    }
  }, [statusFilter, search]);

  useEffect(() => {
    fetchSandboxes();
  }, [fetchSandboxes]);

  const handleStart = useCallback(async (id: string) => {
    try {
      await startSandbox(id);
      await fetchSandboxes();
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "启动沙箱失败");
    }
  }, [fetchSandboxes]);

  const handleStop = useCallback(async (id: string) => {
    try {
      await stopSandbox(id);
      await fetchSandboxes();
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "停止沙箱失败");
    }
  }, [fetchSandboxes]);

  const handleDelete = useCallback(async () => {
    if (!deleteTarget) return;
    try {
      await deleteSandbox(deleteTarget.id);
      setDeleteTarget(null);
      await fetchSandboxes();
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "删除沙箱失败");
    }
  }, [deleteTarget, fetchSandboxes]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="沙箱管理"
        actions={
          <div className="flex items-center gap-2">
            <Button size="sm" variant="outline" onClick={fetchSandboxes} disabled={loading}>
              <RefreshCw className={`mr-1 h-4 w-4 ${loading ? "animate-spin" : ""}`} /> 刷新
            </Button>
            <Button size="sm" asChild>
              <Link to="/sandboxes/new"><Plus className="mr-1 h-4 w-4" /> 新建沙箱</Link>
            </Button>
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Filter Bar */}
        <div className="flex items-center gap-4">
          <Input
            placeholder="搜索沙箱..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="max-w-sm"
          />
          <Select value={statusFilter} onValueChange={setStatusFilter}>
            <SelectTrigger className="w-40">
              <SelectValue placeholder="所有状态" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">所有状态</SelectItem>
              {SANDBOX_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>{s}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="ml-auto text-sm text-muted-foreground">
            共 {totalCount} 个沙箱
          </span>
        </div>

        {/* Content */}
        {loading ? (
          <div className="flex items-center justify-center py-16">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center py-16 gap-4">
            <p className="text-destructive">{error}</p>
            <Button variant="outline" onClick={fetchSandboxes}>重试</Button>
          </div>
        ) : sandboxes.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 gap-4">
            <Container className="h-12 w-12 text-muted-foreground" />
            <p className="text-muted-foreground">暂无沙箱实例</p>
            <Button asChild>
              <Link to="/sandboxes/new">创建第一个沙箱</Link>
            </Button>
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>镜像</TableHead>
                <TableHead>资源</TableHead>
                <TableHead>Pod</TableHead>
                <TableHead>最后活跃</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sandboxes.map((sb) => (
                <TableRow key={sb.id}>
                  <TableCell>
                    <Link
                      to={`/sandboxes/${sb.id}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {sb.name}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <SandboxStatusBadge status={sb.status} />
                  </TableCell>
                  <TableCell className="font-mono text-xs max-w-48 truncate">
                    {sb.image}
                  </TableCell>
                  <TableCell className="text-sm">
                    {sb.cpuCores} CPU / {sb.memoryMib} MiB
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    {sb.podName ?? "—"}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {sb.lastActivityAt
                      ? new Date(sb.lastActivityAt).toLocaleString("zh-CN")
                      : "—"}
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex items-center justify-end gap-1">
                      {sb.status === "Stopped" && (
                        <Button size="sm" variant="outline" onClick={() => handleStart(sb.id)}>
                          启动
                        </Button>
                      )}
                      {sb.status === "Running" && (
                        <Button size="sm" variant="outline" onClick={() => handleStop(sb.id)}>
                          停止
                        </Button>
                      )}
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-destructive"
                        onClick={() => setDeleteTarget(sb)}
                        disabled={sb.status === "Running"}
                      >
                        删除
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>

      <DeleteSandboxDialog
        sandbox={deleteTarget}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
