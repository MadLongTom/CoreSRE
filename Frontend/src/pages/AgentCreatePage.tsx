import { useState, useRef, useEffect } from "react";
import { useNavigate } from "react-router";
import { ArrowLeft, Loader2, Plus, X, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import ProviderModelSelect from "@/components/agents/ProviderModelSelect";
import { PageHeader } from "@/components/layout/PageHeader";
import { createAgent, resolveAgentCard, ApiError } from "@/lib/api/agents";
import type {
  AgentType,
  CreateAgentRequest,
  AgentSkill,
  AgentInterface,
  SecurityScheme,
  ResolvedAgentCard,
} from "@/types/agent";

const TYPE_DESCRIPTIONS: Record<AgentType, string> = {
  A2A: "Agent-to-Agent 协议，通过 HTTP 端点暴露技能",
  ChatClient: "基于 LLM 的聊天客户端 Agent",
  Workflow: "基于工作流引擎的编排 Agent",
};

export default function AgentCreatePage() {
  const navigate = useNavigate();
  const [step, setStep] = useState<1 | 2>(1);
  const [selectedType, setSelectedType] = useState<AgentType | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);

  // Common fields
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  // A2A fields
  const [endpoint, setEndpoint] = useState("");
  const [skills, setSkills] = useState<AgentSkill[]>([
    { name: "", description: "" },
  ]);
  const [interfaces, setInterfaces] = useState<AgentInterface[]>([
    { protocol: "", path: "" },
  ]);
  const [securitySchemes, setSecuritySchemes] = useState<SecurityScheme[]>([]);

  // Resolve state
  const [resolving, setResolving] = useState(false);
  const [resolveError, setResolveError] = useState<string | null>(null);
  const [resolvedCard, setResolvedCard] = useState<ResolvedAgentCard | null>(null);
  const [useUserUrl, setUseUserUrl] = useState(true);
  const abortControllerRef = useRef<AbortController | null>(null);

  // Cleanup abort controller on unmount
  useEffect(() => {
    return () => {
      abortControllerRef.current?.abort();
    };
  }, []);

  // ChatClient fields
  const [providerId, setProviderId] = useState<string | null>(null);
  const [modelId, setModelId] = useState("");
  const [instructions, setInstructions] = useState("");
  const [toolRefs, setToolRefs] = useState("");

  // Workflow fields
  const [workflowRef, setWorkflowRef] = useState("");

  const handleSelectType = (type: AgentType) => {
    setSelectedType(type);
    setStep(2);
  };

  const handleResolve = async () => {
    // Validate URL
    if (!endpoint.trim()) {
      setResolveError("请输入 Endpoint URL");
      return;
    }
    if (!isValidUrl(endpoint.trim())) {
      setResolveError("Endpoint 必须是合法的 HTTP/HTTPS URL");
      return;
    }

    // Cancel any previous in-flight request
    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setResolving(true);
    setResolveError(null);
    setResolvedCard(null);

    try {
      const result = await resolveAgentCard(endpoint.trim(), controller.signal);
      if (result.success && result.data) {
        const card = result.data;
        setResolvedCard(card);
        setUseUserUrl(true);

        // Auto-fill skills
        if (card.skills.length > 0) {
          setSkills(card.skills.map((s) => ({ name: s.name, description: s.description ?? "" })));
        }
        // Auto-fill interfaces
        if (card.interfaces.length > 0) {
          setInterfaces(card.interfaces.map((i) => ({ protocol: i.protocol, path: i.path ?? "" })));
        }
        // Auto-fill security schemes
        if (card.securitySchemes.length > 0) {
          setSecuritySchemes(card.securitySchemes.map((s) => ({ type: s.type, parameters: s.parameters ?? "" })));
        }

        // US3: Pre-fill name/description only if empty
        if (!name.trim() && card.name) {
          setName(card.name);
        }
        if (!description.trim() && card.description) {
          setDescription(card.description);
        }
      } else {
        setResolveError(result.message ?? "解析失败");
      }
    } catch (err) {
      if (controller.signal.aborted) return; // user cancelled, ignore
      const apiErr = err as ApiError;
      if (apiErr.status === 502) {
        setResolveError("无法连接到远程 Agent 端点");
      } else if (apiErr.status === 504) {
        setResolveError("请求超时，远程端点未响应");
      } else if (apiErr.status === 422) {
        setResolveError("远程端点返回的数据无法解析为 AgentCard");
      } else {
        setResolveError(apiErr.message ?? "解析失败，请重试");
      }
    } finally {
      if (!controller.signal.aborted) {
        setResolving(false);
      }
    }
  };

  const handleSubmit = async () => {
    // Client-side validation
    const validationErrors: string[] = [];
    if (!name.trim()) validationErrors.push("名称不能为空");
    if (name.length > 200) validationErrors.push("名称不能超过 200 个字符");
    if (selectedType === "A2A" && endpoint && !isValidUrl(endpoint)) {
      validationErrors.push("Endpoint 必须是合法的 URL");
    }

    if (validationErrors.length > 0) {
      setErrors(validationErrors);
      return;
    }

    setSubmitting(true);
    setErrors([]);

    const request: CreateAgentRequest = {
      name: name.trim(),
      description: description.trim() || undefined,
      agentType: selectedType!,
    };

    if (selectedType === "A2A") {
      // Respect URL override: if resolvedCard exists and user chose AgentCard URL, use that
      const finalEndpoint =
        resolvedCard && !useUserUrl
          ? resolvedCard.url
          : endpoint.trim();
      request.endpoint = finalEndpoint || undefined;
      request.agentCard = {
        skills: skills.filter((s) => s.name.trim()),
        interfaces: interfaces.filter((i) => i.protocol.trim()),
        securitySchemes: securitySchemes.filter((s) => s.type.trim()),
      };
    } else if (selectedType === "ChatClient") {
      request.llmConfig = {
        providerId: providerId ?? undefined,
        modelId: modelId.trim(),
        instructions: instructions.trim() || undefined,
        toolRefs: toolRefs
          .split(",")
          .map((r) => r.trim())
          .filter(Boolean),
      };
    } else if (selectedType === "Workflow") {
      request.workflowRef = workflowRef.trim() || undefined;
    }

    try {
      const result = await createAgent(request);
      if (result.success && result.data) {
        navigate(`/agents/${result.data.id}`);
      } else {
        setErrors(result.errors ?? [result.message ?? "创建失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setErrors(["Agent 名称已被占用"]);
      } else {
        setErrors(
          apiErr.errors ?? [apiErr.message ?? "创建失败，请重试"],
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建 Agent"
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => (step === 2 ? setStep(1) : navigate("/agents"))}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
      <div className="space-y-6">
      {/* Step 1: Type selection */}
      {step === 1 && (
        <div className="grid gap-4 md:grid-cols-3">
          {(Object.keys(TYPE_DESCRIPTIONS) as AgentType[]).map((type) => (
            <Card
              key={type}
              className="cursor-pointer transition-shadow hover:shadow-md"
              onClick={() => handleSelectType(type)}
            >
              <CardHeader>
                <CardTitle className="text-lg">{type}</CardTitle>
                <CardDescription>{TYPE_DESCRIPTIONS[type]}</CardDescription>
              </CardHeader>
            </Card>
          ))}
        </div>
      )}

      {/* Step 2: Form */}
      {step === 2 && selectedType && (
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

          {/* Common fields + type-specific */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Common fields */}
          <Card>
            <CardHeader>\n              <CardTitle>基本信息</CardTitle>\n            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="name">名称 *</Label>
                  <Input
                    id="name"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="Agent 名称"
                    maxLength={200}
                  />
                </div>
                <div className="space-y-2">
                  <Label className="text-muted-foreground text-xs">类型</Label>
                  <p className="text-sm font-medium pt-1">{selectedType}</p>
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="description">描述</Label>
                <Textarea
                  id="description"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="可选描述"
                  rows={2}
                />
              </div>
            </CardContent>
          </Card>

          {/* A2A-specific fields */}
          {selectedType === "A2A" && (
            <Card>
              <CardHeader>
                <CardTitle>A2A 配置</CardTitle>
              </CardHeader>
              <CardContent className="space-y-6">
                <div className="space-y-2">
                  <Label htmlFor="endpoint">Endpoint</Label>
                  <div className="flex gap-2">
                    <Input
                      id="endpoint"
                      value={endpoint}
                      onChange={(e) => {
                        setEndpoint(e.target.value);
                        // Reset resolve state when URL changes
                        setResolvedCard(null);
                        setResolveError(null);
                      }}
                      placeholder="https://example.com/agent"
                      type="url"
                      className="flex-1"
                    />
                    <Button
                      variant="outline"
                      onClick={handleResolve}
                      disabled={resolving || !endpoint.trim()}
                      type="button"
                    >
                      {resolving ? (
                        <Loader2 className="mr-1 h-4 w-4 animate-spin" />
                      ) : (
                        <Search className="mr-1 h-4 w-4" />
                      )}
                      解析
                    </Button>
                  </div>
                  {resolveError && (
                    <p className="text-sm text-destructive">{resolveError}</p>
                  )}
                  {resolvedCard && (
                    <p className="text-sm text-muted-foreground">
                      ✓ 已解析: {resolvedCard.name} (v{resolvedCard.version})
                    </p>
                  )}
                </div>

                {/* URL Override Switch — only show when resolved URL differs from input */}
                {resolvedCard && resolvedCard.url !== endpoint.trim() && (
                  <div className="flex items-center gap-2 rounded-md border p-3 bg-muted/50">
                    <input
                      type="checkbox"
                      id="urlOverride"
                      checked={useUserUrl}
                      onChange={(e) => setUseUserUrl(e.target.checked)}
                      className="h-4 w-4 rounded border-gray-300"
                    />
                    <Label htmlFor="urlOverride" className="text-sm font-normal cursor-pointer">
                      使用我输入的 URL 覆盖 AgentCard 中的 URL
                    </Label>
                    <span className="ml-auto text-xs text-muted-foreground">
                      AgentCard URL: {resolvedCard.url}
                    </span>
                  </div>
                )}

                <Separator />

                {/* Skills */}
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <Label>Skills</Label>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        setSkills([...skills, { name: "", description: "" }])
                      }
                    >
                      <Plus className="mr-1 h-3 w-3" />
                      添加
                    </Button>
                  </div>
                  {skills.map((skill, i) => (
                    <div key={i} className="flex gap-2">
                      <Input
                        placeholder="Skill 名称"
                        value={skill.name}
                        onChange={(e) => {
                          const updated = [...skills];
                          updated[i] = { ...updated[i], name: e.target.value };
                          setSkills(updated);
                        }}
                      />
                      <Input
                        placeholder="描述（可选）"
                        value={skill.description ?? ""}
                        onChange={(e) => {
                          const updated = [...skills];
                          updated[i] = {
                            ...updated[i],
                            description: e.target.value,
                          };
                          setSkills(updated);
                        }}
                      />
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() =>
                          setSkills(skills.filter((_, j) => j !== i))
                        }
                        disabled={skills.length <= 1}
                      >
                        <X className="h-4 w-4" />
                      </Button>
                    </div>
                  ))}
                </div>

                <Separator />

                {/* Interfaces */}
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <Label>Interfaces</Label>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        setInterfaces([
                          ...interfaces,
                          { protocol: "", path: "" },
                        ])
                      }
                    >
                      <Plus className="mr-1 h-3 w-3" />
                      添加
                    </Button>
                  </div>
                  {interfaces.map((iface, i) => (
                    <div key={i} className="flex gap-2">
                      <Input
                        placeholder="Protocol (e.g. HTTP)"
                        value={iface.protocol}
                        onChange={(e) => {
                          const updated = [...interfaces];
                          updated[i] = {
                            ...updated[i],
                            protocol: e.target.value,
                          };
                          setInterfaces(updated);
                        }}
                      />
                      <Input
                        placeholder="Path（可选）"
                        value={iface.path ?? ""}
                        onChange={(e) => {
                          const updated = [...interfaces];
                          updated[i] = {
                            ...updated[i],
                            path: e.target.value,
                          };
                          setInterfaces(updated);
                        }}
                      />
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() =>
                          setInterfaces(interfaces.filter((_, j) => j !== i))
                        }
                        disabled={interfaces.length <= 1}
                      >
                        <X className="h-4 w-4" />
                      </Button>
                    </div>
                  ))}
                </div>

                <Separator />

                {/* Security Schemes */}
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <Label>Security Schemes</Label>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        setSecuritySchemes([
                          ...securitySchemes,
                          { type: "", parameters: "" },
                        ])
                      }
                    >
                      <Plus className="mr-1 h-3 w-3" />
                      添加
                    </Button>
                  </div>
                  {securitySchemes.map((scheme, i) => (
                    <div key={i} className="flex gap-2">
                      <Input
                        placeholder="Type (e.g. Bearer)"
                        value={scheme.type}
                        onChange={(e) => {
                          const updated = [...securitySchemes];
                          updated[i] = {
                            ...updated[i],
                            type: e.target.value,
                          };
                          setSecuritySchemes(updated);
                        }}
                      />
                      <Input
                        placeholder="Parameters（可选）"
                        value={scheme.parameters ?? ""}
                        onChange={(e) => {
                          const updated = [...securitySchemes];
                          updated[i] = {
                            ...updated[i],
                            parameters: e.target.value,
                          };
                          setSecuritySchemes(updated);
                        }}
                      />
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() =>
                          setSecuritySchemes(
                            securitySchemes.filter((_, j) => j !== i),
                          )
                        }
                      >
                        <X className="h-4 w-4" />
                      </Button>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          )}

          {/* ChatClient-specific fields */}
          {selectedType === "ChatClient" && (
            <Card>
              <CardHeader>
                <CardTitle>LLM 配置</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                <ProviderModelSelect
                  providerId={providerId}
                  modelId={modelId}
                  onProviderChange={setProviderId}
                  onModelChange={setModelId}
                />
                <div className="space-y-2">
                  <Label htmlFor="instructions">Instructions</Label>
                  <Textarea
                    id="instructions"
                    value={instructions}
                    onChange={(e) => setInstructions(e.target.value)}
                    placeholder="System prompt / instructions"
                    rows={4}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="toolRefs">Tool Refs</Label>
                  <Input
                    id="toolRefs"
                    value={toolRefs}
                    onChange={(e) => setToolRefs(e.target.value)}
                    placeholder="逗号分隔的 GUID（可选）"
                  />
                </div>
              </CardContent>
            </Card>
          )}

          {/* Workflow-specific fields */}
          {selectedType === "Workflow" && (
            <Card>
              <CardHeader>
                <CardTitle>Workflow 配置</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="workflowRef">Workflow Ref</Label>
                  <Input
                    id="workflowRef"
                    value={workflowRef}
                    onChange={(e) => setWorkflowRef(e.target.value)}
                    placeholder="Workflow GUID（可选）"
                  />
                </div>
              </CardContent>
            </Card>
          )}
          </div>

          {/* Submit */}
          <div className="flex justify-end gap-3">
            <Button
              variant="outline"
              onClick={() => navigate("/agents")}
              disabled={submitting}
            >
              取消
            </Button>
            <Button onClick={handleSubmit} disabled={submitting}>
              {submitting && (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              )}
              创建 Agent
            </Button>
          </div>
        </div>
      )}
    </div>
    </div>
    </div>
  );
}

function isValidUrl(str: string): boolean {
  try {
    const url = new URL(str);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}
