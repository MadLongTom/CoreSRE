import { useCallback, useEffect, useState, useMemo } from "react";
import { Link, useNavigate, useParams, useBlocker } from "react-router";
import {
  ArrowLeft,
  Loader2,
  XCircle,
  Pencil,
  RefreshCw,
  Play,
  Key,
  Globe,
  Clock,
  Plus,
  Trash2,
  ChevronDown,
  ChevronRight,
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
import { PageHeader } from "@/components/layout/PageHeader";
import {
  getToolById,
  updateTool,
  getMcpTools,
  invokeTool,
  ApiError,
} from "@/lib/api/tools";
import { DeleteToolDialog } from "@/components/tools/DeleteToolDialog";
import {
  MCP_TRANSPORT_TYPES,
  AUTH_TYPES,
  HTTP_METHODS,
} from "@/types/tool";
import type {
  ToolRegistration,
  McpToolItem,
  TransportType,
  AuthType,
  ToolInvocationResult,
} from "@/types/tool";

const transportLabel: Record<string, string> = {
  Rest: "REST",
  StreamableHttp: "Streamable HTTP",
  Sse: "SSE",
  AutoDetect: "自动检测",
  Stdio: "Stdio",
};

const authLabel: Record<string, string> = {
  None: "无认证",
  ApiKey: "API Key",
  Bearer: "Bearer Token",
  OAuth2: "OAuth 2.0",
};

export default function ToolDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [tool, setTool] = useState<ToolRegistration | null>(null);
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
  const [editEndpoint, setEditEndpoint] = useState("");
  const [editHttpMethod, setEditHttpMethod] = useState("POST");
  const [editTransportType, setEditTransportType] =
    useState<TransportType>("StreamableHttp");
  const [editAuthType, setEditAuthType] = useState<AuthType>("None");
  const [editCredential, setEditCredential] = useState("");
  const [editApiKeyHeader, setEditApiKeyHeader] = useState("");
  const [editTokenEndpoint, setEditTokenEndpoint] = useState("");
  const [editClientId, setEditClientId] = useState("");
  const [editClientSecret, setEditClientSecret] = useState("");

  // MCP tools discovery
  const [mcpTools, setMcpTools] = useState<McpToolItem[]>([]);
  const [discoveringMcp, setDiscoveringMcp] = useState(false);
  const [mcpError, setMcpError] = useState<string | null>(null);

  // Invoke state — MCP
  const [invokeToolName, setInvokeToolName] = useState("");
  const [invokeParams, setInvokeParams] = useState<
    Record<string, string>
  >({});
  const [invoking, setInvoking] = useState(false);
  const [invokeResult, setInvokeResult] = useState<ToolInvocationResult | null>(
    null,
  );
  const [invokeError, setInvokeError] = useState<string | null>(null);

  // Invoke state — REST dynamic params
  const [restParams, setRestParams] = useState<
    { name: string; value: string; location: "query" | "header" | "body" }[]
  >([]);

  // MCP tool schema expand state
  const [expandedTools, setExpandedTools] = useState<Set<string>>(new Set());

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
      if (leave) blocker.proceed();
      else blocker.reset();
    }
  }, [blocker]);

  // ── Fetch tool ──

  const fetchTool = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    setNotFound(false);
    try {
      const result = await getToolById(id);
      if (result.success && result.data) {
        setTool(result.data);
      } else {
        setError(result.message ?? "加载失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 404) setNotFound(true);
      else setError(apiErr.message);
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    fetchTool();
  }, [fetchTool]);

  // ── Edit helpers ──

  const startEditing = () => {
    if (!tool) return;
    setEditName(tool.name);
    setEditDescription(tool.description ?? "");
    setEditEndpoint(tool.connectionConfig.endpoint);
    setEditHttpMethod(tool.connectionConfig.httpMethod ?? "POST");
    setEditTransportType(
      tool.connectionConfig.transportType as TransportType,
    );
    setEditAuthType(tool.authConfig.authType as AuthType);
    setEditCredential("");
    setEditApiKeyHeader(tool.authConfig.apiKeyHeaderName ?? "");
    setEditTokenEndpoint(tool.authConfig.tokenEndpoint ?? "");
    setEditClientId(tool.authConfig.clientId ?? "");
    setEditClientSecret("");
    setEditing(true);
    setDirty(false);
    setSaveErrors([]);
  };

  const cancelEditing = () => {
    setEditing(false);
    setDirty(false);
    setSaveErrors([]);
  };

  const markDirty = () => {
    if (!dirty) setDirty(true);
  };

  const handleSave = async () => {
    if (!tool || !id) return;

    const validationErrors: string[] = [];
    if (!editName.trim()) validationErrors.push("名称不能为空");
    if (!editEndpoint.trim()) validationErrors.push("端点地址不能为空");
    if (validationErrors.length > 0) {
      setSaveErrors(validationErrors);
      return;
    }

    setSaving(true);
    setSaveErrors([]);

    const isRest = tool.toolType === "RestApi";

    try {
      const result = await updateTool(id, {
        name: editName.trim(),
        description: editDescription.trim() || undefined,
        connectionConfig: {
          endpoint: editEndpoint.trim(),
          transportType: isRest ? "Rest" : editTransportType,
          httpMethod: isRest ? editHttpMethod : undefined,
        },
        authConfig: {
          authType: editAuthType,
          credential:
            editAuthType === "ApiKey" || editAuthType === "Bearer"
              ? editCredential.trim() || undefined
              : undefined,
          apiKeyHeaderName:
            editAuthType === "ApiKey"
              ? editApiKeyHeader.trim() || undefined
              : undefined,
          tokenEndpoint:
            editAuthType === "OAuth2"
              ? editTokenEndpoint.trim()
              : undefined,
          clientId:
            editAuthType === "OAuth2" ? editClientId.trim() : undefined,
          clientSecret:
            editAuthType === "OAuth2"
              ? editClientSecret.trim() || undefined
              : undefined,
        },
      });
      if (result.success && result.data) {
        setTool(result.data);
        setEditing(false);
        setDirty(false);
      } else {
        setSaveErrors(result.errors ?? [result.message ?? "保存失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setSaveErrors(["工具名称已被占用"]);
      } else {
        setSaveErrors(apiErr.errors ?? [apiErr.message ?? "保存失败，请重试"]);
      }
    } finally {
      setSaving(false);
    }
  };

  // ── MCP discovery ──

  const handleDiscoverMcp = async () => {
    if (!id) return;
    setDiscoveringMcp(true);
    setMcpError(null);
    try {
      const result = await getMcpTools(id);
      if (result.success && result.data) {
        setMcpTools(result.data);
      } else {
        setMcpError(result.message ?? "MCP 工具发现失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setMcpError(apiErr.message ?? "MCP 工具发现失败");
    } finally {
      setDiscoveringMcp(false);
    }
  };

  // ── Invoke ──

  // Parse inputSchema properties for a given MCP tool
  const selectedMcpTool = useMemo(
    () => mcpTools.find((t) => t.toolName === invokeToolName),
    [mcpTools, invokeToolName],
  );

  const schemaProperties = useMemo(() => {
    const schema = selectedMcpTool?.inputSchema as
      | { properties?: Record<string, { type?: string; description?: string; enum?: string[] }>; required?: string[] }
      | undefined;
    if (!schema?.properties) return [];
    const required = new Set(schema.required ?? []);
    return Object.entries(schema.properties).map(([name, prop]) => ({
      name,
      type: prop.type ?? "string",
      description: prop.description ?? "",
      required: required.has(name),
      enumValues: prop.enum,
    }));
  }, [selectedMcpTool]);

  // When MCP tool selection changes, reset param values
  useEffect(() => {
    const initial: Record<string, string> = {};
    for (const p of schemaProperties) {
      initial[p.name] = "";
    }
    setInvokeParams(initial);
  }, [schemaProperties]);

  const handleInvoke = async () => {
    if (!id) return;

    setInvoking(true);
    setInvokeError(null);
    setInvokeResult(null);

    try {
      if (tool?.toolType === "McpServer") {
        // MCP invoke: assemble from form fields
        const params: Record<string, unknown> = {};
        for (const [key, val] of Object.entries(invokeParams)) {
          if (val.trim() !== "") {
            // Try to parse as JSON (for numbers, booleans, objects)
            try {
              params[key] = JSON.parse(val);
            } catch {
              params[key] = val;
            }
          }
        }

        const result = await invokeTool(id, {
          mcpToolName: invokeToolName.trim() || undefined,
          parameters: params,
        });
        if (result.success && result.data) {
          setInvokeResult(result.data);
        } else {
          setInvokeError(result.message ?? "调用失败");
        }
      } else {
        // REST invoke: separate params by location
        const bodyParams: Record<string, unknown> = {};
        const queryParams: Record<string, string> = {};
        const headerParams: Record<string, string> = {};

        for (const rp of restParams) {
          if (!rp.name.trim()) continue;
          switch (rp.location) {
            case "query":
              queryParams[rp.name] = rp.value;
              break;
            case "header":
              headerParams[rp.name] = rp.value;
              break;
            case "body":
              try {
                bodyParams[rp.name] = JSON.parse(rp.value);
              } catch {
                bodyParams[rp.name] = rp.value;
              }
              break;
          }
        }

        const result = await invokeTool(id, {
          parameters: bodyParams,
          queryParameters:
            Object.keys(queryParams).length > 0 ? queryParams : undefined,
          headerParameters:
            Object.keys(headerParams).length > 0 ? headerParams : undefined,
        });
        if (result.success && result.data) {
          setInvokeResult(result.data);
        } else {
          setInvokeError(result.message ?? "调用失败");
        }
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setInvokeError(apiErr.message ?? "调用失败");
    } finally {
      setInvoking(false);
    }
  };

  // Toggle expand/collapse for MCP tool schema
  const toggleToolExpand = (toolId: string) => {
    setExpandedTools((prev) => {
      const next = new Set(prev);
      if (next.has(toolId)) next.delete(toolId);
      else next.add(toolId);
      return next;
    });
  };

  // Parse schema properties helper for display in tool cards
  const getToolSchemaProps = (inputSchema: unknown) => {
    const schema = inputSchema as
      | { properties?: Record<string, { type?: string; description?: string; enum?: string[] }>; required?: string[] }
      | undefined;
    if (!schema?.properties) return [];
    const required = new Set(schema.required ?? []);
    return Object.entries(schema.properties).map(([name, prop]) => ({
      name,
      type: prop.type ?? "string",
      description: prop.description ?? "",
      required: required.has(name),
      enumValues: prop.enum,
    }));
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
        <h2 className="text-xl font-semibold">工具未找到</h2>
        <Button asChild variant="outline">
          <Link to="/tools">返回列表</Link>
        </Button>
      </div>
    );
  }

  if (error || !tool) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-destructive">{error ?? "加载失败"}</p>
        <Button variant="outline" onClick={fetchTool}>
          重试
        </Button>
      </div>
    );
  }

  const isMcp = tool.toolType === "McpServer";
  const isRest = tool.toolType === "RestApi";

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title={editing ? "编辑工具" : tool.name}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/tools">
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
        <div className="space-y-6">
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

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Left Column: Basic Info + Connection */}
          <div className="space-y-6">
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
                    <Label htmlFor="edit-desc">描述</Label>
                    <Textarea
                      id="edit-desc"
                      value={editDescription}
                      onChange={(e) => {
                        setEditDescription(e.target.value);
                        markDirty();
                      }}
                      rows={3}
                    />
                  </div>
                </>
              ) : (
                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <Label className="text-muted-foreground text-xs">ID</Label>
                    <p className="font-mono">{tool.id}</p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      名称
                    </Label>
                    <p className="font-medium">{tool.name}</p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      描述
                    </Label>
                    <p>{tool.description || "—"}</p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      类型
                    </Label>
                    <Badge variant="outline">
                      {isRest ? "REST API" : "MCP Server"}
                    </Badge>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      状态
                    </Label>
                    <Badge
                      variant={
                        tool.status === "Active" ? "default" : "secondary"
                      }
                    >
                      {tool.status}
                    </Badge>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      <Clock className="mr-1 inline h-3 w-3" />
                      创建时间
                    </Label>
                    <p>{new Date(tool.createdAt).toLocaleString()}</p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      更新时间
                    </Label>
                    <p>
                      {tool.updatedAt
                        ? new Date(tool.updatedAt).toLocaleString()
                        : "—"}
                    </p>
                  </div>
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
                  <div className="space-y-2">
                    <Label htmlFor="edit-endpoint">端点地址 *</Label>
                    <Input
                      id="edit-endpoint"
                      value={editEndpoint}
                      onChange={(e) => {
                        setEditEndpoint(e.target.value);
                        markDirty();
                      }}
                      type="url"
                    />
                  </div>
                  {isRest && (
                    <div className="space-y-2">
                      <Label>HTTP Method</Label>
                      <Select
                        value={editHttpMethod}
                        onValueChange={(v) => {
                          setEditHttpMethod(v);
                          markDirty();
                        }}
                      >
                        <SelectTrigger className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {HTTP_METHODS.map((m) => (
                            <SelectItem key={m} value={m}>
                              {m}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                  )}
                  {isMcp && (
                    <div className="space-y-2">
                      <Label>传输方式</Label>
                      <Select
                        value={editTransportType}
                        onValueChange={(v) => {
                          setEditTransportType(v as TransportType);
                          markDirty();
                        }}
                      >
                        <SelectTrigger className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {MCP_TRANSPORT_TYPES.map((t) => (
                            <SelectItem key={t} value={t}>
                              {transportLabel[t] ?? t}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                  )}
                </>
              ) : (
                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      <Globe className="mr-1 inline h-3 w-3" />
                      端点
                    </Label>
                    <p className="font-mono break-all">
                      {tool.connectionConfig.endpoint}
                    </p>
                  </div>
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      传输方式
                    </Label>
                    <p>
                      {transportLabel[tool.connectionConfig.transportType] ??
                        tool.connectionConfig.transportType}
                    </p>
                  </div>
                  {isRest && (
                    <div>
                      <Label className="text-muted-foreground text-xs">
                        HTTP Method
                      </Label>
                      <Badge variant="outline">
                        {tool.connectionConfig.httpMethod}
                      </Badge>
                    </div>
                  )}
                </div>
              )}
            </CardContent>
          </Card>
          </div>

          {/* Right Column: Auth + Edit actions */}
          <div className="space-y-6">
          {/* Auth Config (view mode only) */}
          {!editing && (
            <Card>
              <CardHeader>
                <CardTitle>认证配置</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <Label className="text-muted-foreground text-xs">
                      <Key className="mr-1 inline h-3 w-3" />
                      认证方式
                    </Label>
                    <p>
                      {authLabel[tool.authConfig.authType] ??
                        tool.authConfig.authType}
                    </p>
                  </div>
                  {tool.authConfig.hasCredential && (
                    <div>
                      <Label className="text-muted-foreground text-xs">
                        凭证
                      </Label>
                      <p className="font-mono">
                        {tool.authConfig.maskedCredential ?? "••••••"}
                      </p>
                    </div>
                  )}
                  {tool.authConfig.apiKeyHeaderName && (
                    <div>
                      <Label className="text-muted-foreground text-xs">
                        Header 名称
                      </Label>
                      <p className="font-mono">
                        {tool.authConfig.apiKeyHeaderName}
                      </p>
                    </div>
                  )}
                  {tool.authConfig.tokenEndpoint && (
                    <div>
                      <Label className="text-muted-foreground text-xs">
                        Token Endpoint
                      </Label>
                      <p className="font-mono break-all">
                        {tool.authConfig.tokenEndpoint}
                      </p>
                    </div>
                  )}
                  {tool.authConfig.clientId && (
                    <div>
                      <Label className="text-muted-foreground text-xs">
                        Client ID
                      </Label>
                      <p className="font-mono">{tool.authConfig.clientId}</p>
                    </div>
                  )}
                </div>
              </CardContent>
            </Card>
          )}

          {/* Auth Config (edit mode) */}
          {editing && (
            <Card>
              <CardHeader>
                <CardTitle>认证配置</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <Label>认证方式</Label>
                  <Select
                    value={editAuthType}
                    onValueChange={(v) => {
                      setEditAuthType(v as AuthType);
                      markDirty();
                    }}
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {AUTH_TYPES.map((a) => (
                        <SelectItem key={a} value={a}>
                          {authLabel[a] ?? a}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                {editAuthType === "ApiKey" && (
                  <>
                    <div className="space-y-2">
                      <Label htmlFor="edit-apikey">
                        API Key（留空保留原值）
                      </Label>
                      <Input
                        id="edit-apikey"
                        value={editCredential}
                        onChange={(e) => {
                          setEditCredential(e.target.value);
                          markDirty();
                        }}
                        type="password"
                        placeholder="留空则保留当前凭证"
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="edit-header">Header 名称</Label>
                      <Input
                        id="edit-header"
                        value={editApiKeyHeader}
                        onChange={(e) => {
                          setEditApiKeyHeader(e.target.value);
                          markDirty();
                        }}
                        placeholder="X-API-Key"
                      />
                    </div>
                  </>
                )}

                {editAuthType === "Bearer" && (
                  <div className="space-y-2">
                    <Label htmlFor="edit-bearer">
                      Bearer Token（留空保留原值）
                    </Label>
                    <Input
                      id="edit-bearer"
                      value={editCredential}
                      onChange={(e) => {
                        setEditCredential(e.target.value);
                        markDirty();
                      }}
                      type="password"
                      placeholder="留空则保留当前凭证"
                    />
                  </div>
                )}

                {editAuthType === "OAuth2" && (
                  <>
                    <div className="space-y-2">
                      <Label htmlFor="edit-tokenUrl">Token Endpoint</Label>
                      <Input
                        id="edit-tokenUrl"
                        value={editTokenEndpoint}
                        onChange={(e) => {
                          setEditTokenEndpoint(e.target.value);
                          markDirty();
                        }}
                        type="url"
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="edit-clientId">Client ID</Label>
                      <Input
                        id="edit-clientId"
                        value={editClientId}
                        onChange={(e) => {
                          setEditClientId(e.target.value);
                          markDirty();
                        }}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="edit-clientSecret">
                        Client Secret（留空保留原值）
                      </Label>
                      <Input
                        id="edit-clientSecret"
                        value={editClientSecret}
                        onChange={(e) => {
                          setEditClientSecret(e.target.value);
                          markDirty();
                        }}
                        type="password"
                        placeholder="留空则保留当前凭证"
                      />
                    </div>
                  </>
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
                {saving && (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                )}
                保存
              </Button>
            </div>
          )}
          </div>
          </div>

          {/* MCP Tools */}
          {!editing && isMcp && (
            <Card>
              <CardHeader>
                <div className="flex items-center justify-between">
                  <CardTitle>
                    MCP 工具
                    {tool.mcpToolCount > 0 && (
                      <Badge variant="secondary" className="ml-2">
                        {tool.mcpToolCount}
                      </Badge>
                    )}
                  </CardTitle>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handleDiscoverMcp}
                    disabled={discoveringMcp}
                  >
                    {discoveringMcp ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <RefreshCw className="mr-2 h-4 w-4" />
                    )}
                    发现工具
                  </Button>
                </div>
              </CardHeader>
              <CardContent>
                {mcpError && (
                  <div className="mb-3 rounded-md border border-destructive/50 bg-destructive/10 p-3">
                    <p className="text-sm text-destructive">{mcpError}</p>
                  </div>
                )}
                {tool.discoveryError && (
                  <div className="mb-3 rounded-md border border-destructive/50 bg-destructive/10 p-3">
                    <p className="text-sm text-destructive">
                      上次发现错误：{tool.discoveryError}
                    </p>
                  </div>
                )}
                {mcpTools.length === 0 && tool.mcpToolCount === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    暂无 MCP 工具。点击"发现工具"从 MCP 服务器获取工具列表。
                  </p>
                ) : mcpTools.length > 0 ? (
                  <div className="space-y-2">
                    {mcpTools.map((mt) => {
                      const isExpanded = expandedTools.has(mt.id);
                      const props = getToolSchemaProps(mt.inputSchema);
                      return (
                        <div
                          key={mt.id}
                          className="rounded-md border overflow-hidden"
                        >
                          <button
                            type="button"
                            className="flex w-full items-center justify-between p-3 text-left hover:bg-muted/50 transition-colors"
                            onClick={() => toggleToolExpand(mt.id)}
                          >
                            <div className="flex items-center gap-2">
                              {props.length > 0 ? (
                                isExpanded ? (
                                  <ChevronDown className="h-4 w-4 text-muted-foreground shrink-0" />
                                ) : (
                                  <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
                                )
                              ) : (
                                <span className="w-4" />
                              )}
                              <span className="font-medium font-mono text-sm">
                                {mt.toolName}
                              </span>
                              {props.length > 0 && (
                                <Badge variant="secondary" className="text-xs">
                                  {props.length} 参数
                                </Badge>
                              )}
                            </div>
                            <div className="flex gap-1">
                              {mt.annotations?.readOnly && (
                                <Badge variant="secondary">只读</Badge>
                              )}
                              {mt.annotations?.destructive && (
                                <Badge variant="destructive">破坏性</Badge>
                              )}
                              {mt.annotations?.idempotent && (
                                <Badge variant="outline">幂等</Badge>
                              )}
                            </div>
                          </button>
                          {mt.description && (
                            <p className="px-3 pb-2 text-xs text-muted-foreground ml-6">
                              {mt.description}
                            </p>
                          )}
                          {isExpanded && props.length > 0 && (
                            <div className="border-t bg-muted/30 px-3 py-2">
                              <table className="w-full text-xs">
                                <thead>
                                  <tr className="text-muted-foreground">
                                    <th className="text-left py-1 pr-3 font-medium">参数名</th>
                                    <th className="text-left py-1 pr-3 font-medium">类型</th>
                                    <th className="text-left py-1 pr-3 font-medium">必填</th>
                                    <th className="text-left py-1 font-medium">说明</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {props.map((p) => (
                                    <tr key={p.name} className="border-t border-border/50">
                                      <td className="py-1.5 pr-3 font-mono font-medium">
                                        {p.name}
                                      </td>
                                      <td className="py-1.5 pr-3">
                                        <Badge variant="outline" className="text-xs">
                                          {p.type}
                                        </Badge>
                                        {p.enumValues && (
                                          <span className="ml-1 text-muted-foreground">
                                            [{p.enumValues.join(", ")}]
                                          </span>
                                        )}
                                      </td>
                                      <td className="py-1.5 pr-3">
                                        {p.required ? (
                                          <span className="text-destructive font-medium">是</span>
                                        ) : (
                                          <span className="text-muted-foreground">否</span>
                                        )}
                                      </td>
                                      <td className="py-1.5 text-muted-foreground">
                                        {p.description || "—"}
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    已注册 {tool.mcpToolCount} 个 MCP 工具。点击"发现工具"刷新列表。
                  </p>
                )}
              </CardContent>
            </Card>
          )}

          {/* Invoke Tool */}
          {!editing && (
            <Card>
              <CardHeader>
                <CardTitle>调用工具</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                {/* ── MCP invoke: tool selector + schema-driven form ── */}
                {isMcp && (
                  <>
                    <div className="space-y-2">
                      <Label>选择 MCP 工具</Label>
                      {mcpTools.length > 0 ? (
                        <Select
                          value={invokeToolName}
                          onValueChange={setInvokeToolName}
                        >
                          <SelectTrigger className="w-full">
                            <SelectValue placeholder="选择要调用的工具..." />
                          </SelectTrigger>
                          <SelectContent>
                            {mcpTools.map((mt) => (
                              <SelectItem key={mt.id} value={mt.toolName}>
                                <span className="font-mono">{mt.toolName}</span>
                                {mt.description && (
                                  <span className="ml-2 text-muted-foreground text-xs">
                                    — {mt.description.slice(0, 60)}
                                  </span>
                                )}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      ) : (
                        <div className="space-y-2">
                          <Input
                            value={invokeToolName}
                            onChange={(e) => setInvokeToolName(e.target.value)}
                            placeholder={"输入工具名称（先点击「发现工具」可使用下拉选择）"}
                          />
                          <p className="text-xs text-muted-foreground">
                            未发现任何工具。请先点击上方「发现工具」按钮获取工具列表。
                          </p>
                        </div>
                      )}
                    </div>

                    {/* Schema-driven parameter form */}
                    {invokeToolName && schemaProperties.length > 0 && (
                      <div className="space-y-3">
                        <Label className="text-sm font-medium">参数</Label>
                        {schemaProperties.map((sp) => (
                          <div key={sp.name} className="space-y-1">
                            <div className="flex items-center gap-2">
                              <Label
                                htmlFor={`mcp-param-${sp.name}`}
                                className="text-sm font-mono"
                              >
                                {sp.name}
                                {sp.required && (
                                  <span className="text-destructive ml-0.5">*</span>
                                )}
                              </Label>
                              <Badge variant="outline" className="text-xs">
                                {sp.type}
                              </Badge>
                            </div>
                            {sp.enumValues ? (
                              <Select
                                value={invokeParams[sp.name] ?? ""}
                                onValueChange={(v) =>
                                  setInvokeParams((prev) => ({
                                    ...prev,
                                    [sp.name]: v,
                                  }))
                                }
                              >
                                <SelectTrigger className="w-full">
                                  <SelectValue placeholder="选择..." />
                                </SelectTrigger>
                                <SelectContent>
                                  {sp.enumValues.map((ev) => (
                                    <SelectItem key={ev} value={ev}>
                                      {ev}
                                    </SelectItem>
                                  ))}
                                </SelectContent>
                              </Select>
                            ) : (
                              <Input
                                id={`mcp-param-${sp.name}`}
                                value={invokeParams[sp.name] ?? ""}
                                onChange={(e) =>
                                  setInvokeParams((prev) => ({
                                    ...prev,
                                    [sp.name]: e.target.value,
                                  }))
                                }
                                placeholder={sp.description || sp.type}
                                className="font-mono text-sm"
                              />
                            )}
                            {sp.description && (
                              <p className="text-xs text-muted-foreground">
                                {sp.description}
                              </p>
                            )}
                          </div>
                        ))}
                      </div>
                    )}

                    {/* Fallback raw JSON for tools without discovered schema */}
                    {invokeToolName && schemaProperties.length === 0 && (
                      <div className="space-y-2">
                        <Label>参数 (JSON)</Label>
                        <Textarea
                          value={JSON.stringify(invokeParams, null, 2)}
                          onChange={(e) => {
                            try {
                              setInvokeParams(JSON.parse(e.target.value));
                            } catch {
                              // keep invalid input for editing
                            }
                          }}
                          placeholder='{"key": "value"}'
                          rows={4}
                          className="font-mono text-sm"
                        />
                      </div>
                    )}
                  </>
                )}

                {/* ── REST invoke: dynamic parameter rows ── */}
                {isRest && (
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <Label className="text-sm font-medium">请求参数</Label>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() =>
                          setRestParams((prev) => [
                            ...prev,
                            { name: "", value: "", location: "body" },
                          ])
                        }
                      >
                        <Plus className="mr-1 h-3 w-3" />
                        添加参数
                      </Button>
                    </div>
                    {restParams.length === 0 ? (
                      <p className="text-sm text-muted-foreground">
                        暂无参数。点击"添加参数"来配置请求参数。
                      </p>
                    ) : (
                      <div className="space-y-2">
                        <div className="grid grid-cols-[1fr_1fr_100px_32px] gap-2 text-xs text-muted-foreground font-medium">
                          <span>参数名</span>
                          <span>值</span>
                          <span>位置</span>
                          <span />
                        </div>
                        {restParams.map((rp, idx) => (
                          <div
                            key={idx}
                            className="grid grid-cols-[1fr_1fr_100px_32px] gap-2 items-center"
                          >
                            <Input
                              value={rp.name}
                              onChange={(e) =>
                                setRestParams((prev) =>
                                  prev.map((p, i) =>
                                    i === idx
                                      ? { ...p, name: e.target.value }
                                      : p,
                                  ),
                                )
                              }
                              placeholder="key"
                              className="font-mono text-sm"
                            />
                            <Input
                              value={rp.value}
                              onChange={(e) =>
                                setRestParams((prev) =>
                                  prev.map((p, i) =>
                                    i === idx
                                      ? { ...p, value: e.target.value }
                                      : p,
                                  ),
                                )
                              }
                              placeholder="value"
                              className="font-mono text-sm"
                            />
                            <Select
                              value={rp.location}
                              onValueChange={(v) =>
                                setRestParams((prev) =>
                                  prev.map((p, i) =>
                                    i === idx
                                      ? {
                                          ...p,
                                          location: v as
                                            | "query"
                                            | "header"
                                            | "body",
                                        }
                                      : p,
                                  ),
                                )
                              }
                            >
                              <SelectTrigger className="w-full text-xs">
                                <SelectValue />
                              </SelectTrigger>
                              <SelectContent>
                                <SelectItem value="query">Query</SelectItem>
                                <SelectItem value="header">Header</SelectItem>
                                <SelectItem value="body">Body</SelectItem>
                              </SelectContent>
                            </Select>
                            <Button
                              variant="ghost"
                              size="icon"
                              className="h-8 w-8"
                              onClick={() =>
                                setRestParams((prev) =>
                                  prev.filter((_, i) => i !== idx),
                                )
                              }
                            >
                              <Trash2 className="h-3.5 w-3.5 text-muted-foreground hover:text-destructive" />
                            </Button>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                )}

                <Button
                  onClick={handleInvoke}
                  disabled={invoking || (isMcp && !invokeToolName.trim())}
                  size="sm"
                >
                  {invoking ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <Play className="mr-2 h-4 w-4" />
                  )}
                  执行
                </Button>

                {invokeError && (
                  <div className="rounded-md border border-destructive/50 bg-destructive/10 p-3">
                    <p className="text-sm text-destructive">{invokeError}</p>
                  </div>
                )}
                {invokeResult && (
                  <div className="rounded-md border p-3 space-y-2">
                    <div className="flex items-center gap-3 text-sm">
                      <Badge
                        variant={
                          invokeResult.success ? "default" : "destructive"
                        }
                      >
                        {invokeResult.success ? "成功" : "失败"}
                      </Badge>
                      <span className="text-muted-foreground">
                        耗时 {invokeResult.durationMs} ms
                      </span>
                    </div>
                    {invokeResult.error && (
                      <p className="text-sm text-destructive">
                        {invokeResult.error}
                      </p>
                    )}
                    {invokeResult.data != null && (
                      <pre className="rounded bg-muted p-3 text-xs overflow-x-auto max-h-60 overflow-y-auto">
                        {typeof invokeResult.data === "string"
                          ? invokeResult.data
                          : JSON.stringify(invokeResult.data, null, 2)}
                      </pre>
                    )}
                  </div>
                )}
              </CardContent>
            </Card>
          )}

          {/* Delete dialog */}
          {tool && (
            <DeleteToolDialog
              toolId={tool.id}
              toolName={tool.name}
              open={showDelete}
              onOpenChange={setShowDelete}
              onDeleted={() => navigate("/tools")}
            />
          )}
        </div>
      </div>
    </div>
  );
}
