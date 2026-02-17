import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams, useBlocker } from "react-router";
import {
  ArrowLeft,
  Loader2,
  Pencil,
  RefreshCw,
  Play,
  Search,
  Activity,
  XCircle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Checkbox } from "@/components/ui/checkbox";
import { PageHeader } from "@/components/layout/PageHeader";
import {
  getDataSourceById,
  updateDataSource,
  testDataSourceConnection,
  discoverMetadata,
  queryDataSource,
  ApiError,
} from "@/lib/api/datasources";
import { DeleteDataSourceDialog } from "@/components/datasources/DeleteDataSourceDialog";
import {
  categoryLabel,
  statusLabel,
  statusVariant,
  authLabel,
  AUTH_TYPES,
} from "@/types/datasource";
import type {
  DataSourceRegistration,
  DataSourceCategory,
  DataSourceStatus,
  AuthType,
  DataSourceResource,
} from "@/types/datasource";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

export default function DataSourceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [ds, setDs] = useState<DataSourceRegistration | null>(null);
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
  const [editDescription, setEditDescription] = useState("");
  const [editBaseUrl, setEditBaseUrl] = useState("");
  const [editAuthType, setEditAuthType] = useState<AuthType>("None");
  const [editCredential, setEditCredential] = useState("");
  const [editAuthHeaderName, setEditAuthHeaderName] = useState("");
  const [editTlsSkipVerify, setEditTlsSkipVerify] = useState(false);
  const [editTimeoutSeconds, setEditTimeoutSeconds] = useState(30);
  const [editNamespace, setEditNamespace] = useState("");
  const [editOrganization, setEditOrganization] = useState("");
  const [editKubeConfig, setEditKubeConfig] = useState("");
  const [editDefaultNamespace, setEditDefaultNamespace] = useState("");
  const [editMaxResults, setEditMaxResults] = useState<number | undefined>(undefined);
  const [editDefaultStep, setEditDefaultStep] = useState("");
  const [editDefaultIndex, setEditDefaultIndex] = useState("");

  // Test connection state
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<string | null>(null);
  const [testError, setTestError] = useState<string | null>(null);

  // Discover metadata state
  const [discovering, setDiscovering] = useState(false);

  // Query state
  const [queryExpression, setQueryExpression] = useState("");
  const [queryTimeRange, setQueryTimeRange] = useState("5m");
  const [querying, setQuerying] = useState(false);
  const [queryResults, setQueryResults] = useState<DataSourceResource[] | null>(null);
  const [queryError, setQueryError] = useState<string | null>(null);

  // Delete dialog
  const [deleteOpen, setDeleteOpen] = useState(false);

  // Unsaved changes guard
  useBlocker(
    ({ currentLocation, nextLocation }) =>
      dirty && currentLocation.pathname !== nextLocation.pathname,
  );

  const fetchDataSource = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const result = await getDataSourceById(id);
      if (result.success && result.data) {
        setDs(result.data);
        initEditFields(result.data);
      } else {
        setError(result.message ?? "加载失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 404) {
        setNotFound(true);
      } else {
        setError(apiErr.message ?? "加载失败");
      }
    } finally {
      setLoading(false);
    }
  }, [id]);

  const initEditFields = (d: DataSourceRegistration) => {
    setEditName(d.name);
    setEditDescription(d.description ?? "");
    setEditBaseUrl(d.connectionConfig.baseUrl);
    setEditAuthType(d.connectionConfig.authType as AuthType);
    setEditCredential("");
    setEditAuthHeaderName(d.connectionConfig.authHeaderName ?? "");
    setEditTlsSkipVerify(d.connectionConfig.tlsSkipVerify);
    setEditTimeoutSeconds(d.connectionConfig.timeoutSeconds);
    setEditNamespace(d.connectionConfig.namespace ?? "");
    setEditOrganization(d.connectionConfig.organization ?? "");
    setEditKubeConfig("");
    setEditDefaultNamespace(d.defaultQueryConfig?.defaultNamespace ?? "");
    setEditMaxResults(d.defaultQueryConfig?.maxResults);
    setEditDefaultStep(d.defaultQueryConfig?.defaultStep ?? "");
    setEditDefaultIndex(d.defaultQueryConfig?.defaultIndex ?? "");
    setDirty(false);
  };

  useEffect(() => {
    fetchDataSource();
  }, [fetchDataSource]);

  const markDirty = () => {
    if (!dirty) setDirty(true);
  };

  const handleStartEdit = () => {
    if (ds) initEditFields(ds);
    setSaveErrors([]);
    setEditing(true);
  };

  const handleCancelEdit = () => {
    if (ds) initEditFields(ds);
    setEditing(false);
    setSaveErrors([]);
  };

  const handleSave = async () => {
    if (!id || !ds) return;
    const validationErrors: string[] = [];
    if (!editName.trim()) validationErrors.push("名称不能为空");

    if (validationErrors.length > 0) {
      setSaveErrors(validationErrors);
      return;
    }

    setSaving(true);
    setSaveErrors([]);

    try {
      const result = await updateDataSource(id, {
        name: editName.trim(),
        description: editDescription.trim() || undefined,
        connectionConfig: {
          baseUrl: editBaseUrl.trim(),
          authType: editAuthType,
          credential: editCredential.trim() || undefined,
          authHeaderName: editAuthType === "ApiKey" ? editAuthHeaderName.trim() || undefined : undefined,
          tlsSkipVerify: editTlsSkipVerify,
          timeoutSeconds: editTimeoutSeconds,
          namespace: editNamespace.trim() || undefined,
          organization: editOrganization.trim() || undefined,
          kubeConfig: editKubeConfig.trim() || undefined,
        },
        defaultQueryConfig: {
          defaultNamespace: editDefaultNamespace.trim() || undefined,
          maxResults: editMaxResults || undefined,
          defaultStep: editDefaultStep.trim() || undefined,
          defaultIndex: editDefaultIndex.trim() || undefined,
        },
      });

      if (result.success && result.data) {
        setDs(result.data);
        initEditFields(result.data);
        setEditing(false);
      } else {
        setSaveErrors(result.errors ?? [result.message ?? "保存失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setSaveErrors(apiErr.errors ?? [apiErr.message ?? "保存失败"]);
    } finally {
      setSaving(false);
    }
  };

  const handleTestConnection = async () => {
    if (!id) return;
    setTesting(true);
    setTestResult(null);
    setTestError(null);
    try {
      const result = await testDataSourceConnection(id);
      if (result.success && result.data) {
        const d = result.data;
        if (d.isHealthy) {
          setTestResult(
            `连接成功！版本: ${d.version ?? "未知"}, 响应时间: ${d.responseTimeMs ?? "?"}ms`,
          );
        } else {
          setTestError(d.errorMessage ?? "连接失败");
        }
        // Refresh to update health status
        fetchDataSource();
      } else {
        setTestError(result.message ?? "测试失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setTestError(apiErr.message ?? "测试失败");
    } finally {
      setTesting(false);
    }
  };

  const handleDiscover = async () => {
    if (!id) return;
    setDiscovering(true);
    try {
      await discoverMetadata(id);
      fetchDataSource();
    } catch {
      // silently ignore, will be visible in metadata section
    } finally {
      setDiscovering(false);
    }
  };

  const handleQuery = async () => {
    if (!id || !queryExpression.trim()) return;
    setQuerying(true);
    setQueryError(null);
    setQueryResults(null);
    try {
      const result = await queryDataSource(id, {
        expression: queryExpression.trim(),
        timeRange: queryTimeRange.trim() || undefined,
      });
      if (result.success && result.data) {
        setQueryResults(result.data.resources);
      } else {
        setQueryError(result.message ?? "查询失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setQueryError(apiErr.message ?? "查询失败");
    } finally {
      setQuerying(false);
    }
  };

  // ── Rendering ──

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
        <p className="text-muted-foreground">数据源不存在</p>
        <Button variant="outline" onClick={() => navigate("/datasources")}>
          返回列表
        </Button>
      </div>
    );
  }

  if (error || !ds) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-destructive">{error ?? "加载失败"}</p>
        <Button variant="outline" onClick={fetchDataSource}>
          <RefreshCw className="mr-2 h-4 w-4" />
          重试
        </Button>
      </div>
    );
  }

  const isK8s = ds.product === "Kubernetes";
  const isGit = ds.product === "GitHub" || ds.product === "GitLab";
  const isMetrics = ds.category === "Metrics";
  const isLogs = ds.category === "Logs";

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={ds.name}
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/datasources")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
        actions={
          <div className="flex items-center gap-2">
            {!editing ? (
              <>
                <Button variant="outline" size="sm" onClick={handleTestConnection} disabled={testing}>
                  {testing ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <Activity className="mr-2 h-4 w-4" />
                  )}
                  测试连接
                </Button>
                <Button variant="outline" size="sm" onClick={handleDiscover} disabled={discovering}>
                  {discovering ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <Search className="mr-2 h-4 w-4" />
                  )}
                  发现元数据
                </Button>
                <Button variant="outline" size="sm" onClick={handleStartEdit}>
                  <Pencil className="mr-2 h-4 w-4" />
                  编辑
                </Button>
                <Button
                  variant="destructive"
                  size="sm"
                  onClick={() => setDeleteOpen(true)}
                >
                  删除
                </Button>
              </>
            ) : (
              <>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleCancelEdit}
                  disabled={saving}
                >
                  取消
                </Button>
                <Button size="sm" onClick={handleSave} disabled={saving}>
                  {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  保存
                </Button>
              </>
            )}
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        <div className="mx-auto max-w-4xl space-y-6">
          {/* Save / Test Errors */}
          {saveErrors.length > 0 && (
            <div className="rounded-md border border-destructive bg-destructive/10 p-4">
              <ul className="list-disc pl-4 text-sm text-destructive space-y-1">
                {saveErrors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          {testResult && (
            <div className="rounded-md border border-green-500 bg-green-500/10 p-4 text-sm text-green-700 dark:text-green-400">
              {testResult}
            </div>
          )}
          {testError && (
            <div className="rounded-md border border-destructive bg-destructive/10 p-4 text-sm text-destructive">
              连接测试失败: {testError}
            </div>
          )}

          {/* Overview */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-3">
                基本信息
                <Badge variant={statusVariant[ds.status as DataSourceStatus] ?? "secondary"}>
                  {statusLabel[ds.status as DataSourceStatus] ?? ds.status}
                </Badge>
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              {editing ? (
                <>
                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label>名称</Label>
                      <Input
                        value={editName}
                        onChange={(e) => { setEditName(e.target.value); markDirty(); }}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label>分类 / 产品</Label>
                      <Input
                        value={`${categoryLabel[ds.category as DataSourceCategory] ?? ds.category} — ${ds.product}`}
                        disabled
                      />
                    </div>
                  </div>
                  <div className="space-y-2">
                    <Label>描述</Label>
                    <Textarea
                      value={editDescription}
                      onChange={(e) => { setEditDescription(e.target.value); markDirty(); }}
                      rows={2}
                    />
                  </div>
                </>
              ) : (
                <div className="grid grid-cols-2 gap-y-3 text-sm">
                  <div className="text-muted-foreground">名称</div>
                  <div className="font-medium">{ds.name}</div>
                  <div className="text-muted-foreground">描述</div>
                  <div>{ds.description || "—"}</div>
                  <div className="text-muted-foreground">分类</div>
                  <div>
                    <Badge variant="outline">
                      {categoryLabel[ds.category as DataSourceCategory] ?? ds.category}
                    </Badge>
                  </div>
                  <div className="text-muted-foreground">产品</div>
                  <div>{ds.product}</div>
                  <div className="text-muted-foreground">创建时间</div>
                  <div>{formatDate(ds.createdAt)}</div>
                  {ds.updatedAt && (
                    <>
                      <div className="text-muted-foreground">更新时间</div>
                      <div>{formatDate(ds.updatedAt)}</div>
                    </>
                  )}
                </div>
              )}
            </CardContent>
          </Card>

          {/* Connection Config */}
          <Card>
            <CardHeader>
              <CardTitle>连接配置</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              {editing ? (
                <>
                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label>端点 URL</Label>
                      <Input
                        value={editBaseUrl}
                        onChange={(e) => { setEditBaseUrl(e.target.value); markDirty(); }}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label>超时时间 (秒)</Label>
                      <Input
                        type="number"
                        value={editTimeoutSeconds}
                        onChange={(e) => { setEditTimeoutSeconds(parseInt(e.target.value) || 30); markDirty(); }}
                      />
                    </div>
                  </div>

                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label>认证方式</Label>
                      <Select value={editAuthType} onValueChange={(v) => { setEditAuthType(v as AuthType); markDirty(); }}>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {AUTH_TYPES.map((a) => (
                            <SelectItem key={a} value={a}>
                              {authLabel[a]}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                    {editAuthType === "ApiKey" && (
                      <div className="space-y-2">
                        <Label>API Key Header</Label>
                        <Input
                          value={editAuthHeaderName}
                          onChange={(e) => { setEditAuthHeaderName(e.target.value); markDirty(); }}
                          placeholder="X-API-Key"
                        />
                      </div>
                    )}
                  </div>

                  {editAuthType !== "None" && (
                    <div className="space-y-2">
                      <Label>
                        凭据 {ds.connectionConfig.hasCredential && "(留空则保持原有凭据)"}
                      </Label>
                      <Input
                        type="password"
                        value={editCredential}
                        onChange={(e) => { setEditCredential(e.target.value); markDirty(); }}
                        placeholder={ds.connectionConfig.hasCredential ? "••••••••" : "输入凭据..."}
                      />
                    </div>
                  )}

                  {(isK8s || isGit) && (
                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                      {isK8s && (
                        <div className="space-y-2">
                          <Label>默认 Namespace</Label>
                          <Input
                            value={editNamespace}
                            onChange={(e) => { setEditNamespace(e.target.value); markDirty(); }}
                          />
                        </div>
                      )}
                      {isGit && (
                        <div className="space-y-2">
                          <Label>Organization</Label>
                          <Input
                            value={editOrganization}
                            onChange={(e) => { setEditOrganization(e.target.value); markDirty(); }}
                          />
                        </div>
                      )}
                    </div>
                  )}

                  {isK8s && (
                    <div className="space-y-2">
                      <Label>KubeConfig (Base64，留空保持原有)</Label>
                      <Textarea
                        value={editKubeConfig}
                        onChange={(e) => { setEditKubeConfig(e.target.value); markDirty(); }}
                        rows={3}
                        className="font-mono text-xs"
                      />
                    </div>
                  )}

                  <div className="flex items-center space-x-2">
                    <Checkbox
                      checked={editTlsSkipVerify}
                      onCheckedChange={(checked) => { setEditTlsSkipVerify(checked === true); markDirty(); }}
                    />
                    <Label className="text-sm font-normal">跳过 TLS 证书验证</Label>
                  </div>
                </>
              ) : (
                <div className="grid grid-cols-2 gap-y-3 text-sm">
                  <div className="text-muted-foreground">端点 URL</div>
                  <div className="font-mono text-xs break-all">{ds.connectionConfig.baseUrl || "—"}</div>
                  <div className="text-muted-foreground">认证方式</div>
                  <div>{authLabel[ds.connectionConfig.authType as AuthType] ?? ds.connectionConfig.authType}</div>
                  {ds.connectionConfig.hasCredential && (
                    <>
                      <div className="text-muted-foreground">凭据</div>
                      <div className="font-mono text-xs">{ds.connectionConfig.maskedCredential ?? "••••••••"}</div>
                    </>
                  )}
                  {ds.connectionConfig.authHeaderName && (
                    <>
                      <div className="text-muted-foreground">Auth Header</div>
                      <div className="font-mono text-xs">{ds.connectionConfig.authHeaderName}</div>
                    </>
                  )}
                  <div className="text-muted-foreground">超时时间</div>
                  <div>{ds.connectionConfig.timeoutSeconds}s</div>
                  <div className="text-muted-foreground">TLS 跳过验证</div>
                  <div>{ds.connectionConfig.tlsSkipVerify ? "是" : "否"}</div>
                  {ds.connectionConfig.namespace && (
                    <>
                      <div className="text-muted-foreground">Namespace</div>
                      <div>{ds.connectionConfig.namespace}</div>
                    </>
                  )}
                  {ds.connectionConfig.organization && (
                    <>
                      <div className="text-muted-foreground">Organization</div>
                      <div>{ds.connectionConfig.organization}</div>
                    </>
                  )}
                </div>
              )}
            </CardContent>
          </Card>

          {/* Default Query Config */}
          <Card>
            <CardHeader>
              <CardTitle>默认查询配置</CardTitle>
            </CardHeader>
            <CardContent>
              {editing ? (
                <div className="space-y-4">
                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label>默认 Namespace</Label>
                      <Input
                        value={editDefaultNamespace}
                        onChange={(e) => { setEditDefaultNamespace(e.target.value); markDirty(); }}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label>最大结果数</Label>
                      <Input
                        type="number"
                        value={editMaxResults ?? ""}
                        onChange={(e) => { setEditMaxResults(e.target.value ? parseInt(e.target.value) : undefined); markDirty(); }}
                      />
                    </div>
                  </div>
                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                    {isMetrics && (
                      <div className="space-y-2">
                        <Label>默认步长</Label>
                        <Input
                          value={editDefaultStep}
                          onChange={(e) => { setEditDefaultStep(e.target.value); markDirty(); }}
                        />
                      </div>
                    )}
                    {isLogs && (
                      <div className="space-y-2">
                        <Label>默认 Index</Label>
                        <Input
                          value={editDefaultIndex}
                          onChange={(e) => { setEditDefaultIndex(e.target.value); markDirty(); }}
                        />
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-y-3 text-sm">
                  <div className="text-muted-foreground">默认 Namespace</div>
                  <div>{ds.defaultQueryConfig?.defaultNamespace || "—"}</div>
                  <div className="text-muted-foreground">最大结果数</div>
                  <div>{ds.defaultQueryConfig?.maxResults ?? "—"}</div>
                  {isMetrics && (
                    <>
                      <div className="text-muted-foreground">默认步长</div>
                      <div>{ds.defaultQueryConfig?.defaultStep || "—"}</div>
                    </>
                  )}
                  {isLogs && (
                    <>
                      <div className="text-muted-foreground">默认 Index</div>
                      <div>{ds.defaultQueryConfig?.defaultIndex || "—"}</div>
                    </>
                  )}
                </div>
              )}
            </CardContent>
          </Card>

          {/* Health Check */}
          {ds.healthCheck && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-3">
                  健康检查
                  <Badge variant={ds.healthCheck.isHealthy ? "default" : "destructive"}>
                    {ds.healthCheck.isHealthy ? "健康" : "异常"}
                  </Badge>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid grid-cols-2 gap-y-3 text-sm">
                  <div className="text-muted-foreground">最后检查</div>
                  <div>{ds.healthCheck.lastCheckAt ? formatDate(ds.healthCheck.lastCheckAt) : "—"}</div>
                  <div className="text-muted-foreground">版本号</div>
                  <div>{ds.healthCheck.version || "—"}</div>
                  <div className="text-muted-foreground">响应时间</div>
                  <div>{ds.healthCheck.responseTimeMs != null ? `${ds.healthCheck.responseTimeMs}ms` : "—"}</div>
                  {ds.healthCheck.errorMessage && (
                    <>
                      <div className="text-muted-foreground">错误信息</div>
                      <div className="text-destructive">{ds.healthCheck.errorMessage}</div>
                    </>
                  )}
                </div>
              </CardContent>
            </Card>
          )}

          {/* Metadata */}
          {ds.metadata && (
            <Card>
              <CardHeader>
                <CardTitle>发现的元数据</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                {ds.metadata.discoveredAt && (
                  <p className="text-xs text-muted-foreground">
                    发现时间: {formatDate(ds.metadata.discoveredAt)}
                  </p>
                )}
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                  {ds.metadata.labels && ds.metadata.labels.length > 0 && (
                    <div>
                      <Label className="text-xs text-muted-foreground">Labels</Label>
                      <div className="flex flex-wrap gap-1 mt-1">
                        {ds.metadata.labels.slice(0, 20).map((l) => (
                          <Badge key={l} variant="outline" className="text-xs">
                            {l}
                          </Badge>
                        ))}
                        {ds.metadata.labels.length > 20 && (
                          <Badge variant="secondary" className="text-xs">
                            +{ds.metadata.labels.length - 20} more
                          </Badge>
                        )}
                      </div>
                    </div>
                  )}
                  {ds.metadata.namespaces && ds.metadata.namespaces.length > 0 && (
                    <div>
                      <Label className="text-xs text-muted-foreground">Namespaces</Label>
                      <div className="flex flex-wrap gap-1 mt-1">
                        {ds.metadata.namespaces.map((n) => (
                          <Badge key={n} variant="outline" className="text-xs">
                            {n}
                          </Badge>
                        ))}
                      </div>
                    </div>
                  )}
                  {ds.metadata.services && ds.metadata.services.length > 0 && (
                    <div>
                      <Label className="text-xs text-muted-foreground">Services</Label>
                      <div className="flex flex-wrap gap-1 mt-1">
                        {ds.metadata.services.slice(0, 20).map((s) => (
                          <Badge key={s} variant="outline" className="text-xs">
                            {s}
                          </Badge>
                        ))}
                        {ds.metadata.services.length > 20 && (
                          <Badge variant="secondary" className="text-xs">
                            +{ds.metadata.services.length - 20} more
                          </Badge>
                        )}
                      </div>
                    </div>
                  )}
                  {ds.metadata.indices && ds.metadata.indices.length > 0 && (
                    <div>
                      <Label className="text-xs text-muted-foreground">Indices</Label>
                      <div className="flex flex-wrap gap-1 mt-1">
                        {ds.metadata.indices.map((idx) => (
                          <Badge key={idx} variant="outline" className="text-xs">
                            {idx}
                          </Badge>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
                {ds.metadata.availableFunctions && ds.metadata.availableFunctions.length > 0 && (
                  <div>
                    <Label className="text-xs text-muted-foreground">可用函数</Label>
                    <div className="flex flex-wrap gap-1 mt-1">
                      {ds.metadata.availableFunctions.map((fn) => (
                        <Badge key={fn} variant="secondary" className="text-xs font-mono">
                          {fn}
                        </Badge>
                      ))}
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          )}

          {/* Query Panel */}
          {!editing && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <Play className="h-4 w-4" />
                  快速查询
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="flex items-end gap-4">
                  <div className="flex-1 space-y-2">
                    <Label>查询表达式</Label>
                    <Input
                      value={queryExpression}
                      onChange={(e) => setQueryExpression(e.target.value)}
                      placeholder={
                        isK8s
                          ? "kind=Deployment"
                          : isGit
                            ? "Commits"
                            : isMetrics
                              ? "up"
                              : "{app=~\".+\"}"
                      }
                      onKeyDown={(e) => {
                        if (e.key === "Enter") handleQuery();
                      }}
                    />
                  </div>
                  <div className="w-24 space-y-2">
                    <Label>时间范围</Label>
                    <Input
                      value={queryTimeRange}
                      onChange={(e) => setQueryTimeRange(e.target.value)}
                      placeholder="5m"
                    />
                  </div>
                  <Button
                    onClick={handleQuery}
                    disabled={querying || !queryExpression.trim()}
                  >
                    {querying ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Play className="mr-2 h-4 w-4" />
                    )}
                    查询
                  </Button>
                </div>

                {queryError && (
                  <div className="rounded-md border border-destructive bg-destructive/10 p-3 text-sm text-destructive">
                    {queryError}
                  </div>
                )}

                {queryResults && (
                  <div className="space-y-2">
                    <p className="text-sm text-muted-foreground">
                      返回 {queryResults.length} 条结果
                    </p>
                    <div className="max-h-96 overflow-auto rounded-md border">
                      <table className="w-full text-sm">
                        <thead className="bg-muted/50 sticky top-0">
                          <tr>
                            <th className="px-3 py-2 text-left font-medium">Kind</th>
                            <th className="px-3 py-2 text-left font-medium">Name</th>
                            <th className="px-3 py-2 text-left font-medium">Namespace</th>
                            <th className="px-3 py-2 text-left font-medium">Status</th>
                            <th className="px-3 py-2 text-left font-medium">Updated</th>
                          </tr>
                        </thead>
                        <tbody>
                          {queryResults.map((r, i) => (
                            <tr key={i} className="border-t">
                              <td className="px-3 py-2">
                                <Badge variant="outline" className="text-xs">{r.kind}</Badge>
                              </td>
                              <td className="px-3 py-2 font-mono text-xs">{r.name}</td>
                              <td className="px-3 py-2 text-muted-foreground">{r.namespace || "—"}</td>
                              <td className="px-3 py-2">
                                {r.status && (
                                  <Badge
                                    variant={
                                      r.status === "Running" || r.status === "Active" || r.status === "Healthy" || r.status === "success"
                                        ? "default"
                                        : r.status === "Failed" || r.status === "Error" || r.status === "failure"
                                          ? "destructive"
                                          : "secondary"
                                    }
                                    className="text-xs"
                                  >
                                    {r.status}
                                  </Badge>
                                )}
                              </td>
                              <td className="px-3 py-2 text-xs text-muted-foreground">
                                {r.updatedAt ? formatDate(r.updatedAt) : "—"}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          )}
        </div>
      </div>

      {/* Delete Dialog */}
      <DeleteDataSourceDialog
        dataSourceId={ds.id}
        dataSourceName={ds.name}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        onDeleted={() => navigate("/datasources")}
      />
    </div>
  );
}
