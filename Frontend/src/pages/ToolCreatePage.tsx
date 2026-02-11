import { useState } from "react";
import { useNavigate } from "react-router";
import { ArrowLeft, Loader2, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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
import { createTool, ApiError } from "@/lib/api/tools";
import {
  TOOL_TYPES,
  MCP_TRANSPORT_TYPES,
  AUTH_TYPES,
  HTTP_METHODS,
} from "@/types/tool";
import type { ToolType, TransportType, AuthType } from "@/types/tool";

const toolTypeLabel: Record<ToolType, string> = {
  RestApi: "REST API",
  McpServer: "MCP Server",
};

const transportLabel: Record<string, string> = {
  StreamableHttp: "Streamable HTTP",
  Sse: "SSE (Server-Sent Events)",
  AutoDetect: "自动检测",
  Stdio: "Stdio (本地进程)",
};

const authLabel: Record<AuthType, string> = {
  None: "无认证",
  ApiKey: "API Key",
  Bearer: "Bearer Token",
  OAuth2: "OAuth 2.0",
};

export default function ToolCreatePage() {
  const navigate = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);

  // Form fields
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [toolType, setToolType] = useState<ToolType>("RestApi");
  const [endpoint, setEndpoint] = useState("");
  const [httpMethod, setHttpMethod] = useState("POST");
  const [transportType, setTransportType] = useState<TransportType>("StreamableHttp");

  // Auth fields
  const [authType, setAuthType] = useState<AuthType>("None");
  const [credential, setCredential] = useState("");
  const [apiKeyHeaderName, setApiKeyHeaderName] = useState("");
  const [tokenEndpoint, setTokenEndpoint] = useState("");
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");

  // Input parameters (REST API)
  type ParamDef = { name: string; type: string; required: boolean; description: string; location: "query" | "header" | "body" };
  const [inputParams, setInputParams] = useState<ParamDef[]>([]);

  const effectiveTransport = toolType === "RestApi" ? "Rest" : transportType;

  // Build JSON Schema from parameter definitions
  const buildInputSchema = (): string | undefined => {
    if (inputParams.length === 0) return undefined;
    const properties: Record<string, { type: string; description?: string; "x-in"?: string }> = {};
    const required: string[] = [];
    for (const p of inputParams) {
      if (!p.name.trim()) continue;
      properties[p.name] = {
        type: p.type,
        ...(p.description ? { description: p.description } : {}),
        "x-in": p.location,
      };
      if (p.required) required.push(p.name);
    }
    if (Object.keys(properties).length === 0) return undefined;
    return JSON.stringify({
      type: "object",
      properties,
      ...(required.length > 0 ? { required } : {}),
    });
  };

  const handleSubmit = async () => {
    const validationErrors: string[] = [];
    if (!name.trim()) validationErrors.push("名称不能为空");
    if (name.length > 200) validationErrors.push("名称不能超过 200 个字符");
    if (!endpoint.trim()) validationErrors.push("端点地址不能为空");
    if (
      toolType === "RestApi" &&
      endpoint.trim() &&
      !endpoint.trim().startsWith("http://") &&
      !endpoint.trim().startsWith("https://")
    ) {
      validationErrors.push("REST API 端点必须以 http:// 或 https:// 开头");
    }
    if (authType === "ApiKey" && !credential.trim())
      validationErrors.push("API Key 不能为空");
    if (authType === "Bearer" && !credential.trim())
      validationErrors.push("Bearer Token 不能为空");
    if (authType === "OAuth2") {
      if (!tokenEndpoint.trim()) validationErrors.push("Token Endpoint 不能为空");
      if (!clientId.trim()) validationErrors.push("Client ID 不能为空");
      if (!clientSecret.trim()) validationErrors.push("Client Secret 不能为空");
    }

    if (validationErrors.length > 0) {
      setErrors(validationErrors);
      return;
    }

    setSubmitting(true);
    setErrors([]);

    try {
      const result = await createTool({
        name: name.trim(),
        description: description.trim() || undefined,
        toolType,
        connectionConfig: {
          endpoint: endpoint.trim(),
          transportType: effectiveTransport,
          httpMethod: toolType === "RestApi" ? httpMethod : undefined,
        },
        authConfig: {
          authType,
          credential:
            authType === "ApiKey" || authType === "Bearer"
              ? credential.trim()
              : authType === "OAuth2"
                ? undefined
                : undefined,
          apiKeyHeaderName:
            authType === "ApiKey" ? apiKeyHeaderName.trim() || undefined : undefined,
          tokenEndpoint: authType === "OAuth2" ? tokenEndpoint.trim() : undefined,
          clientId: authType === "OAuth2" ? clientId.trim() : undefined,
          clientSecret: authType === "OAuth2" ? clientSecret.trim() : undefined,
        },
        inputSchema: buildInputSchema(),
      });
      if (result.success && result.data) {
        navigate(`/tools/${result.data.id}`);
      } else {
        setErrors(result.errors ?? [result.message ?? "创建失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setErrors(["工具名称已被占用"]);
      } else {
        setErrors(apiErr.errors ?? [apiErr.message ?? "创建失败，请重试"]);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建工具"
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/tools")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        <div className="space-y-6">
          {/* Error display */}
          {errors.length > 0 && (
            <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
              <ul className="list-disc pl-5 text-sm text-destructive space-y-1">
                {errors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Left Column */}
          <div className="space-y-6">
          {/* Basic Info */}
          <Card>
            <CardHeader>
              <CardTitle>基本信息</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="tool-name">名称 *</Label>
                  <Input
                    id="tool-name"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. weather-api, github-mcp"
                    maxLength={200}
                  />
                </div>
                <div className="space-y-2">
                  <Label>工具类型 *</Label>
                  <Select
                    value={toolType}
                    onValueChange={(v) => setToolType(v as ToolType)}
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {TOOL_TYPES.map((t) => (
                        <SelectItem key={t} value={t}>
                          {toolTypeLabel[t]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="tool-desc">描述</Label>
                <Textarea
                  id="tool-desc"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="工具用途描述..."
                  rows={2}
                />
              </div>
            </CardContent>
          </Card>

          {/* Connection Config */}
          <Card>
            <CardHeader>
              <CardTitle>连接配置</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="tool-endpoint">端点地址 *</Label>
                <Input
                  id="tool-endpoint"
                  value={endpoint}
                  onChange={(e) => setEndpoint(e.target.value)}
                  placeholder={
                    toolType === "RestApi"
                      ? "https://api.example.com/v1/resource"
                      : "https://mcp-server.example.com"
                  }
                  type="url"
                />
              </div>

              {toolType === "RestApi" && (
                <div className="space-y-2">
                  <Label>HTTP Method *</Label>
                  <Select value={httpMethod} onValueChange={setHttpMethod}>
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

              {toolType === "McpServer" && (
                <div className="space-y-2">
                  <Label>传输方式 *</Label>
                  <Select
                    value={transportType}
                    onValueChange={(v) => setTransportType(v as TransportType)}
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
                  <p className="text-xs text-muted-foreground">
                    {transportType === "AutoDetect" &&
                      "自动检测：优先尝试 Streamable HTTP，失败后回退到 SSE"}
                    {transportType === "Sse" &&
                      "传统 HTTP + SSE（Server-Sent Events）传输"}
                    {transportType === "StreamableHttp" &&
                      "最新 MCP Streamable HTTP 传输协议"}
                    {transportType === "Stdio" &&
                      "通过标准输入/输出与本地 MCP 进程通信"}
                  </p>
                </div>
              )}
            </CardContent>
          </Card>
          </div>

          {/* Right Column */}
          <div className="space-y-6">
          {/* Input Parameters (REST API) */}
          {toolType === "RestApi" && (
            <Card>
              <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>输入参数</CardTitle>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setInputParams((prev) => [
                      ...prev,
                      { name: "", type: "string", required: false, description: "", location: "query" },
                    ])
                  }
                >
                  <Plus className="mr-1 h-4 w-4" />
                  添加参数
                </Button>
              </CardHeader>
              <CardContent className="space-y-3">
                {inputParams.length === 0 && (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    暂未定义输入参数，点击「添加参数」开始
                  </p>
                )}
                {inputParams.map((param, idx) => (
                  <div key={idx} className="border rounded-md p-3 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-medium">参数 #{idx + 1}</span>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        onClick={() => setInputParams((prev) => prev.filter((_, i) => i !== idx))}
                      >
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1">
                        <Label>参数名 *</Label>
                        <Input
                          value={param.name}
                          onChange={(e) =>
                            setInputParams((prev) =>
                              prev.map((p, i) => (i === idx ? { ...p, name: e.target.value } : p))
                            )
                          }
                          placeholder="paramName"
                        />
                      </div>
                      <div className="space-y-1">
                        <Label>类型</Label>
                        <Select
                          value={param.type}
                          onValueChange={(v) =>
                            setInputParams((prev) =>
                              prev.map((p, i) => (i === idx ? { ...p, type: v } : p))
                            )
                          }
                        >
                          <SelectTrigger className="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {["string", "number", "integer", "boolean", "object", "array"].map((t) => (
                              <SelectItem key={t} value={t}>{t}</SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="space-y-1">
                        <Label>位置</Label>
                        <Select
                          value={param.location}
                          onValueChange={(v) =>
                            setInputParams((prev) =>
                              prev.map((p, i) => (i === idx ? { ...p, location: v as ParamDef["location"] } : p))
                            )
                          }
                        >
                          <SelectTrigger className="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="query">Query</SelectItem>
                            <SelectItem value="header">Header</SelectItem>
                            <SelectItem value="body">Body</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="flex items-end space-x-2 pb-1">
                        <Checkbox
                          id={`param-required-${idx}`}
                          checked={param.required}
                          onCheckedChange={(v) =>
                            setInputParams((prev) =>
                              prev.map((p, i) => (i === idx ? { ...p, required: !!v } : p))
                            )
                          }
                        />
                        <Label htmlFor={`param-required-${idx}`} className="text-sm">必填</Label>
                      </div>
                    </div>
                    <div className="space-y-1">
                      <Label>描述</Label>
                      <Input
                        value={param.description}
                        onChange={(e) =>
                          setInputParams((prev) =>
                            prev.map((p, i) => (i === idx ? { ...p, description: e.target.value } : p))
                          )
                        }
                        placeholder="参数说明"
                      />
                    </div>
                  </div>
                ))}
              </CardContent>
            </Card>
          )}

          {/* Auth Config */}
          <Card>
            <CardHeader>
              <CardTitle>认证配置</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <Label>认证方式</Label>
                <Select
                  value={authType}
                  onValueChange={(v) => setAuthType(v as AuthType)}
                >
                  <SelectTrigger className="w-full">
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

              {authType === "ApiKey" && (
                <>
                  <div className="space-y-2">
                    <Label htmlFor="auth-apikey">API Key *</Label>
                    <Input
                      id="auth-apikey"
                      value={credential}
                      onChange={(e) => setCredential(e.target.value)}
                      type="password"
                      placeholder="your-api-key"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="auth-header">
                      Header 名称（默认 X-API-Key）
                    </Label>
                    <Input
                      id="auth-header"
                      value={apiKeyHeaderName}
                      onChange={(e) => setApiKeyHeaderName(e.target.value)}
                      placeholder="X-API-Key"
                    />
                  </div>
                </>
              )}

              {authType === "Bearer" && (
                <div className="space-y-2">
                  <Label htmlFor="auth-bearer">Bearer Token *</Label>
                  <Input
                    id="auth-bearer"
                    value={credential}
                    onChange={(e) => setCredential(e.target.value)}
                    type="password"
                    placeholder="eyJ..."
                  />
                </div>
              )}

              {authType === "OAuth2" && (
                <>
                  <div className="space-y-2">
                    <Label htmlFor="auth-tokenUrl">Token Endpoint *</Label>
                    <Input
                      id="auth-tokenUrl"
                      value={tokenEndpoint}
                      onChange={(e) => setTokenEndpoint(e.target.value)}
                      placeholder="https://auth.example.com/oauth/token"
                      type="url"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="auth-clientId">Client ID *</Label>
                    <Input
                      id="auth-clientId"
                      value={clientId}
                      onChange={(e) => setClientId(e.target.value)}
                      placeholder="client-id"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="auth-clientSecret">Client Secret *</Label>
                    <Input
                      id="auth-clientSecret"
                      value={clientSecret}
                      onChange={(e) => setClientSecret(e.target.value)}
                      type="password"
                      placeholder="client-secret"
                    />
                  </div>
                </>
              )}
            </CardContent>
          </Card>
          </div>
          </div>

          {/* Submit */}
          <div className="flex justify-end gap-3">
            <Button
              variant="outline"
              onClick={() => navigate("/tools")}
              disabled={submitting}
            >
              取消
            </Button>
            <Button onClick={handleSubmit} disabled={submitting}>
              {submitting && (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              )}
              创建工具
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
