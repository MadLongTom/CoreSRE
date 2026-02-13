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
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import { SandboxErrorAlert, type SandboxError } from "@/components/sandboxes/SandboxErrorAlert";
import {
  getSandboxById,
  startSandbox,
  stopSandbox,
  deleteSandbox,
} from "@/lib/api/sandboxes";
import type { SandboxInstance } from "@/types/sandbox";
import { SandboxStatusBadge } from "@/components/sandboxes/SandboxStatusBadge";
import { SandboxTerminal } from "@/components/sandboxes/SandboxTerminal";

export default function SandboxDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [sandbox, setSandbox] = useState<SandboxInstance | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<SandboxError | null>(null);
  const [actionLoading, setActionLoading] = useState(false);

  const fetchSandbox = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const result = await getSandboxById(id);
      if (result.success && result.data) {
        setSandbox(result.data);
      } else {
        setError({ message: result.message ?? "加载沙箱详情失败" });
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError({ message: apiErr.message ?? "加载沙箱详情失败", hints: apiErr.errors });
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
      setError({ message: apiErr.message ?? "启动沙箱失败", hints: apiErr.errors });
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
      setError({ message: apiErr.message ?? "停止沙箱失败", hints: apiErr.errors });
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
      setError({ message: apiErr.message ?? "删除沙箱失败", hints: apiErr.errors });
      setActionLoading(false);
    }
  }, [id, navigate]);

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
        <SandboxErrorAlert error={error} className="max-w-lg" />
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
        {error && <SandboxErrorAlert error={error} />}

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

        {/* Web Terminal */}
        {sandbox.status === "Running" && (
          <Card className="flex flex-col" style={{ height: "480px" }}>
            <CardHeader className="shrink-0 pb-2">
              <CardTitle className="flex items-center gap-2">
                <Terminal className="h-5 w-5" /> 终端
              </CardTitle>
            </CardHeader>
            <CardContent className="flex-1 min-h-0 pb-3">
              <SandboxTerminal sandboxId={sandbox.id} />
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
