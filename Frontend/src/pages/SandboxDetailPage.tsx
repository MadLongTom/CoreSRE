import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import {
  ArrowLeft,
  Loader2,
  Play,
  Square,
  Trash2,
  Terminal,
  RefreshCw,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import {
  getSandboxById,
  startSandbox,
  stopSandbox,
  deleteSandbox,
  execSandbox,
} from "@/lib/api/sandboxes";
import type { SandboxInstance, SandboxExecResult } from "@/types/sandbox";
import { SandboxStatusBadge } from "@/components/sandboxes/SandboxStatusBadge";

export default function SandboxDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [sandbox, setSandbox] = useState<SandboxInstance | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState(false);

  // Exec state
  const [execCommand, setExecCommand] = useState("");
  const [execResults, setExecResults] = useState<
    Array<{ command: string; result: SandboxExecResult }>
  >([]);
  const [execRunning, setExecRunning] = useState(false);

  const fetchSandbox = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const result = await getSandboxById(id);
      if (result.success && result.data) {
        setSandbox(result.data);
      } else {
        setError(result.message ?? "加载沙箱详情失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "加载沙箱详情失败");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    fetchSandbox();
  }, [fetchSandbox]);

  const handleStart = useCallback(async () => {
    if (!id) return;
    setActionLoading(true);
    try {
      const result = await startSandbox(id);
      if (result.success && result.data) setSandbox(result.data);
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "启动沙箱失败");
    } finally {
      setActionLoading(false);
    }
  }, [id]);

  const handleStop = useCallback(async () => {
    if (!id) return;
    setActionLoading(true);
    try {
      const result = await stopSandbox(id);
      if (result.success && result.data) setSandbox(result.data);
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "停止沙箱失败");
    } finally {
      setActionLoading(false);
    }
  }, [id]);

  const handleDelete = useCallback(async () => {
    if (!id) return;
    setActionLoading(true);
    try {
      await deleteSandbox(id);
      navigate("/sandboxes");
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "删除沙箱失败");
      setActionLoading(false);
    }
  }, [id, navigate]);

  const handleExec = useCallback(async () => {
    if (!id || !execCommand.trim()) return;
    setExecRunning(true);
    try {
      const parts = execCommand.trim().split(/\s+/);
      const result = await execSandbox(id, {
        command: parts[0],
        args: parts.slice(1),
      });
      if (result.success && result.data) {
        setExecResults((prev) => [
          ...prev,
          { command: execCommand, result: result.data! },
        ]);
        setExecCommand("");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setExecResults((prev) => [
        ...prev,
        {
          command: execCommand,
          result: { exitCode: -1, stdout: "", stderr: apiErr.message ?? "执行失败" },
        },
      ]);
    } finally {
      setExecRunning(false);
    }
  }, [id, execCommand]);

  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error && !sandbox) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-destructive">{error}</p>
        <Button variant="outline" onClick={fetchSandbox}>重试</Button>
      </div>
    );
  }

  if (!sandbox) return null;

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={sandbox.name}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/sandboxes"><ArrowLeft className="h-4 w-4" /></Link>
          </Button>
        }
        actions={
          <div className="flex items-center gap-2">
            <SandboxStatusBadge status={sandbox.status} />
            <Button size="sm" variant="outline" onClick={fetchSandbox} disabled={actionLoading}>
              <RefreshCw className="mr-1 h-4 w-4" /> 刷新
            </Button>
            {sandbox.status === "Stopped" && (
              <Button size="sm" onClick={handleStart} disabled={actionLoading}>
                <Play className="mr-1 h-4 w-4" /> 启动
              </Button>
            )}
            {sandbox.status === "Running" && (
              <Button size="sm" variant="secondary" onClick={handleStop} disabled={actionLoading}>
                <Square className="mr-1 h-4 w-4" /> 停止
              </Button>
            )}
            {sandbox.status !== "Running" && (
              <Button
                size="sm"
                variant="destructive"
                onClick={handleDelete}
                disabled={actionLoading}
              >
                <Trash2 className="mr-1 h-4 w-4" /> 删除
              </Button>
            )}
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {error && (
          <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
        )}

        {/* Info Cards */}
        <div className="grid gap-6 md:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>基本信息</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <InfoRow label="ID" value={sandbox.id} mono />
              <InfoRow label="类型" value={sandbox.sandboxType} />
              <InfoRow label="镜像" value={sandbox.image} mono />
              <InfoRow label="命名空间" value={sandbox.k8sNamespace} mono />
              <InfoRow label="Pod" value={sandbox.podName ?? "—"} mono />
              <InfoRow
                label="创建时间"
                value={new Date(sandbox.createdAt).toLocaleString("zh-CN")}
              />
              {sandbox.updatedAt && (
                <InfoRow
                  label="更新时间"
                  value={new Date(sandbox.updatedAt).toLocaleString("zh-CN")}
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>资源配置</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <InfoRow label="CPU" value={`${sandbox.cpuCores} 核`} />
              <InfoRow label="内存" value={`${sandbox.memoryMib} MiB`} />
              <InfoRow label="自动停止" value={`${sandbox.autoStopMinutes} 分钟`} />
              <InfoRow label="持久化工作区" value={sandbox.persistWorkspace ? "是" : "否"} />
              <InfoRow label="关联 Agent" value={sandbox.agentId ?? "无"} mono={!!sandbox.agentId} />
              <InfoRow
                label="最后活跃"
                value={
                  sandbox.lastActivityAt
                    ? new Date(sandbox.lastActivityAt).toLocaleString("zh-CN")
                    : "—"
                }
              />
            </CardContent>
          </Card>
        </div>

        {/* Terminal / Exec Section */}
        {sandbox.status === "Running" && (
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Terminal className="h-5 w-5" /> 命令执行
              </CardTitle>
              <CardDescription>在沙箱中执行命令</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              {/* Output */}
              {execResults.length > 0 && (
                <div className="rounded-md bg-black p-4 font-mono text-sm text-green-400 max-h-96 overflow-y-auto space-y-3">
                  {execResults.map((entry, i) => (
                    <div key={i}>
                      <div className="text-gray-400">$ {entry.command}</div>
                      {entry.result.stdout && <pre className="whitespace-pre-wrap">{entry.result.stdout}</pre>}
                      {entry.result.stderr && (
                        <pre className="whitespace-pre-wrap text-red-400">{entry.result.stderr}</pre>
                      )}
                      <div className="text-gray-600 text-xs">
                        exit code: {entry.result.exitCode}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {/* Input */}
              <div className="flex gap-2">
                <Input
                  className="font-mono"
                  placeholder="输入命令..."
                  value={execCommand}
                  onChange={(e) => setExecCommand(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && !execRunning) handleExec();
                  }}
                  disabled={execRunning}
                />
                <Button onClick={handleExec} disabled={execRunning || !execCommand.trim()}>
                  {execRunning ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    "执行"
                  )}
                </Button>
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}

function InfoRow({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <span className="text-sm text-muted-foreground shrink-0">{label}</span>
      <span className={`text-sm text-right truncate ${mono ? "font-mono" : ""}`}>
        {value}
      </span>
    </div>
  );
}
