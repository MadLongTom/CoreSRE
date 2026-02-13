import { useCallback, useState } from "react";
import { useNavigate, Link } from "react-router";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import { createSandbox } from "@/lib/api/sandboxes";
import type { CreateSandboxRequest } from "@/types/sandbox";

export default function SandboxCreatePage() {
  const navigate = useNavigate();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [form, setForm] = useState<CreateSandboxRequest>({
    name: "",
    sandboxType: "SimpleBox",
    image: "python:3.12-slim",
    cpuCores: 1,
    memoryMib: 512,
    k8sNamespace: "coresre-sandbox",
    autoStopMinutes: 30,
    persistWorkspace: true,
  });

  const updateField = useCallback(
    <K extends keyof CreateSandboxRequest>(key: K, value: CreateSandboxRequest[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }));
    },
    [],
  );

  const handleSubmit = useCallback(async () => {
    if (!form.name.trim()) {
      setError("名称不能为空");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const result = await createSandbox(form);
      if (result.success && result.data) {
        navigate(`/sandboxes/${result.data.id}`);
      } else {
        setError(result.message ?? "创建沙箱失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "创建沙箱失败");
    } finally {
      setSaving(false);
    }
  }, [form, navigate]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建沙箱"
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/sandboxes"><ArrowLeft className="h-4 w-4" /></Link>
          </Button>
        }
        actions={
          <Button onClick={handleSubmit} disabled={saving}>
            {saving ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
            创建
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6 max-w-2xl">
        {error && (
          <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
        )}

        <Card>
          <CardHeader>
            <CardTitle>基本信息</CardTitle>
            <CardDescription>配置沙箱名称和容器镜像</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="name">名称</Label>
              <Input
                id="name"
                value={form.name}
                onChange={(e) => updateField("name", e.target.value)}
                placeholder="my-sandbox"
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="image">容器镜像</Label>
              <Input
                id="image"
                value={form.image ?? ""}
                onChange={(e) => updateField("image", e.target.value)}
                placeholder="python:3.12-slim"
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="namespace">Kubernetes 命名空间</Label>
              <Input
                id="namespace"
                value={form.k8sNamespace ?? ""}
                onChange={(e) => updateField("k8sNamespace", e.target.value)}
              />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>资源配置</CardTitle>
            <CardDescription>设置 CPU、内存和自动停止时间</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="cpu">CPU 核数</Label>
                <Input
                  id="cpu"
                  type="number"
                  min={1}
                  max={8}
                  value={form.cpuCores ?? 1}
                  onChange={(e) => updateField("cpuCores", Number(e.target.value))}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="memory">内存 (MiB)</Label>
                <Input
                  id="memory"
                  type="number"
                  min={256}
                  max={8192}
                  step={256}
                  value={form.memoryMib ?? 512}
                  onChange={(e) => updateField("memoryMib", Number(e.target.value))}
                />
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="auto-stop">自动停止 (分钟)</Label>
              <Input
                id="auto-stop"
                type="number"
                min={5}
                max={1440}
                value={form.autoStopMinutes ?? 30}
                onChange={(e) => updateField("autoStopMinutes", Number(e.target.value))}
              />
            </div>
            <div className="flex items-center gap-3">
              <Switch
                id="persist"
                checked={form.persistWorkspace ?? true}
                onCheckedChange={(checked) => updateField("persistWorkspace", checked)}
              />
              <Label htmlFor="persist">持久化工作区（停止后保留文件）</Label>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
