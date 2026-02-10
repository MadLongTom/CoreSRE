import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams, useBlocker } from "react-router";
import {
  ArrowLeft,
  Loader2,
  XCircle,
  Clock,
  Key,
  Globe,
  Pencil,
  RefreshCw,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  getProviderById,
  updateProvider,
  discoverModels,
  ApiError,
} from "@/lib/api/providers";
import { DeleteProviderDialog } from "@/components/providers/DeleteProviderDialog";
import { PageHeader } from "@/components/layout/PageHeader";
import type { LlmProvider } from "@/types/provider";

export default function ProviderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [provider, setProvider] = useState<LlmProvider | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Edit state
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveErrors, setSaveErrors] = useState<string[]>([]);
  const [dirty, setDirty] = useState(false);

  // Editable fields
  const [editName, setEditName] = useState("");
  const [editBaseUrl, setEditBaseUrl] = useState("");
  const [editApiKey, setEditApiKey] = useState("");

  // Discover state
  const [discovering, setDiscovering] = useState(false);
  const [discoverError, setDiscoverError] = useState<string | null>(null);

  // Delete dialog
  const [showDelete, setShowDelete] = useState(false);

  // Block navigation with unsaved changes
  const blocker = useBlocker(dirty && editing);

  useEffect(() => {
    if (!dirty || !editing) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty, editing]);

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

  const fetchProvider = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    setNotFound(false);
    try {
      const result = await getProviderById(id);
      if (result.success && result.data) {
        setProvider(result.data);
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
    fetchProvider();
  }, [fetchProvider]);

  const startEditing = () => {
    if (!provider) return;
    setEditName(provider.name);
    setEditBaseUrl(provider.baseUrl);
    setEditApiKey("");
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
    if (!provider || !id) return;

    const validationErrors: string[] = [];
    if (!editName.trim()) validationErrors.push("名称不能为空");
    if (editName.length > 200) validationErrors.push("名称不能超过 200 个字符");
    if (!editBaseUrl.trim()) validationErrors.push("Base URL 不能为空");
    if (
      editBaseUrl.trim() &&
      !editBaseUrl.trim().startsWith("http://") &&
      !editBaseUrl.trim().startsWith("https://")
    ) {
      validationErrors.push("Base URL 必须以 http:// 或 https:// 开头");
    }
    if (validationErrors.length > 0) {
      setSaveErrors(validationErrors);
      return;
    }

    setSaving(true);
    setSaveErrors([]);

    try {
      const result = await updateProvider(id, {
        name: editName.trim(),
        baseUrl: editBaseUrl.trim(),
        apiKey: editApiKey.trim() || undefined,
      });
      if (result.success && result.data) {
        setProvider(result.data);
        setEditing(false);
        setDirty(false);
      } else {
        setSaveErrors(result.errors ?? [result.message ?? "保存失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setSaveErrors(["Provider 名称已被占用"]);
      } else {
        setSaveErrors(apiErr.errors ?? [apiErr.message ?? "保存失败，请重试"]);
      }
    } finally {
      setSaving(false);
    }
  };

  const handleDiscover = async () => {
    if (!id) return;
    setDiscovering(true);
    setDiscoverError(null);
    try {
      const result = await discoverModels(id);
      if (result.success && result.data) {
        setProvider(result.data);
      } else {
        setDiscoverError(result.message ?? "模型发现失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setDiscoverError(apiErr.message ?? "模型发现失败，请检查 Provider 配置");
    } finally {
      setDiscovering(false);
    }
  };

  const markDirty = () => {
    if (!dirty) setDirty(true);
  };

  // ---------- Rendering ----------

  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <XCircle className="h-12 w-12 text-muted-foreground" />
        <h2 className="text-xl font-semibold">Provider 未找到</h2>
        <Button asChild variant="outline">
          <Link to="/providers">返回列表</Link>
        </Button>
      </div>
    );
  }

  if (error || !provider) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-destructive">{error ?? "加载失败"}</p>
        <Button variant="outline" onClick={fetchProvider}>
          重试
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={editing ? "编辑 Provider" : provider.name}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/providers">
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
        }
        actions={
          !editing ? (
            <>
              <Button variant="outline" size="sm" onClick={startEditing}>
                <Pencil className="mr-2 h-4 w-4" />
                编辑
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={() => setShowDelete(true)}
              >
                删除
              </Button>
            </>
          ) : undefined
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
      <div className="max-w-3xl space-y-6">

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

      {/* Basic Info */}
      <Card>
        <CardHeader>
          <CardTitle>基本信息</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {editing ? (
            <>
              <div className="space-y-2">
                <Label htmlFor="edit-name">名称 *</Label>
                <Input
                  id="edit-name"
                  value={editName}
                  onChange={(e) => {
                    setEditName(e.target.value);
                    markDirty();
                  }}
                  maxLength={200}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="edit-baseUrl">Base URL *</Label>
                <Input
                  id="edit-baseUrl"
                  value={editBaseUrl}
                  onChange={(e) => {
                    setEditBaseUrl(e.target.value);
                    markDirty();
                  }}
                  type="url"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="edit-apiKey">
                  API Key（留空保留原值）
                </Label>
                <Input
                  id="edit-apiKey"
                  value={editApiKey}
                  onChange={(e) => {
                    setEditApiKey(e.target.value);
                    markDirty();
                  }}
                  type="password"
                  placeholder="留空则保留当前 API Key"
                />
              </div>
            </>
          ) : (
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <Label className="text-muted-foreground text-xs">ID</Label>
                <p className="font-mono">{provider.id}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">名称</Label>
                <p className="font-medium">{provider.name}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">
                  <Globe className="mr-1 inline h-3 w-3" />
                  Base URL
                </Label>
                <p className="font-mono">{provider.baseUrl}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">
                  <Key className="mr-1 inline h-3 w-3" />
                  API Key
                </Label>
                <p className="font-mono">{provider.maskedApiKey}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">
                  创建时间
                </Label>
                <p>{new Date(provider.createdAt).toLocaleString()}</p>
              </div>
              <div>
                <Label className="text-muted-foreground text-xs">
                  更新时间
                </Label>
                <p>
                  {provider.updatedAt
                    ? new Date(provider.updatedAt).toLocaleString()
                    : "—"}
                </p>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Discovered Models */}
      {!editing && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>已发现模型</CardTitle>
              <div className="flex items-center gap-2">
                {provider.modelsRefreshedAt && (
                  <span className="flex items-center gap-1 text-xs text-muted-foreground">
                    <Clock className="h-3 w-3" />
                    上次刷新：
                    {new Date(provider.modelsRefreshedAt).toLocaleString()}
                  </span>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleDiscover}
                  disabled={discovering}
                >
                  {discovering ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <RefreshCw className="mr-2 h-4 w-4" />
                  )}
                  发现模型
                </Button>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {discoverError && (
              <div className="mb-3 rounded-md border border-destructive/50 bg-destructive/10 p-3">
                <p className="text-sm text-destructive">{discoverError}</p>
              </div>
            )}
            {provider.discoveredModels.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                暂无已发现的模型。点击"发现模型"从 Provider 获取模型列表。
              </p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {provider.discoveredModels.map((model) => (
                  <Badge key={model} variant="secondary">
                    {model}
                  </Badge>
                ))}
              </div>
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
      {provider && (
        <DeleteProviderDialog
          providerId={provider.id}
          providerName={provider.name}
          open={showDelete}
          onOpenChange={setShowDelete}
          onDeleted={() => navigate("/providers")}
        />
      )}
    </div>
    </div>
    </div>
  );
}
