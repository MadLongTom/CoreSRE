import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { PageHeader } from "@/components/layout/PageHeader";
import { MatcherEditor } from "@/components/alert-rules/MatcherEditor";
import { ContextInitEditor } from "@/components/alert-rules/ContextInitEditor";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Combobox, type ComboboxOption } from "@/components/ui/combobox";
import { ArrowLeft, Loader2, Save } from "lucide-react";
import type {
  AlertMatcher,
  AlertRuleDto,
  ContextInitItem,
  CreateAlertRuleRequest,
  UpdateAlertRuleRequest,
} from "@/types/alert-rule";
import {
  INCIDENT_SEVERITIES,
  INCIDENT_ROUTES,
  ROUTE_LABELS,
} from "@/types/alert-rule";
import type { ApiResult, AgentSummary } from "@/types/agent";
import type { SkillRegistration } from "@/types/skill";

const API = "/api/alert-rules";

export default function AlertRuleFormPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isEdit = !!id;

  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Form state
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState("P3");
  const [route, setRoute] = useState("SopExecution");
  const [matchers, setMatchers] = useState<AlertMatcher[]>([]);
  const [cooldownMinutes, setCooldownMinutes] = useState(30);
  const [sopId, setSopId] = useState("");
  const [responderAgentId, setResponderAgentId] = useState("");
  const [teamAgentId, setTeamAgentId] = useState("");
  const [summarizerAgentId, setSummarizerAgentId] = useState("");
  const [contextProviders, setContextProviders] = useState<ContextInitItem[]>([]);

  // Reference data for dropdowns
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [skills, setSkills] = useState<SkillRegistration[]>([]);

  // Fetch agents & skills on mount
  useEffect(() => {
    const fetchRefData = async () => {
      try {
        const [agentResp, skillResp] = await Promise.all([
          fetch("/api/agents"),
          fetch("/api/skills?pageSize=200"),
        ]);
        const agentResult: ApiResult<AgentSummary[]> = await agentResp.json();
        const skillResult: ApiResult<{
          items: SkillRegistration[];
          totalCount: number;
        }> = await skillResp.json();
        if (agentResult.success && agentResult.data)
          setAgents(agentResult.data);
        if (skillResult.success && skillResult.data?.items)
          setSkills(skillResult.data.items);
      } catch {
        // non-fatal — dropdowns will just be empty
      }
    };
    fetchRefData();
  }, []);

  // Build combobox options
  const skillOptions = useMemo<ComboboxOption[]>(
    () =>
      skills.map((s) => ({
        value: s.id,
        label: s.name,
        description: s.category,
      })),
    [skills],
  );

  const nonTeamAgentOptions = useMemo<ComboboxOption[]>(
    () =>
      agents
        .filter((a) => a.agentType !== "Team")
        .map((a) => ({
          value: a.id,
          label: a.name,
          description: a.agentType,
        })),
    [agents],
  );

  const teamAgentOptions = useMemo<ComboboxOption[]>(
    () =>
      agents
        .filter((a) => a.agentType === "Team")
        .map((a) => ({
          value: a.id,
          label: a.name,
          description: "Team Agent",
        })),
    [agents],
  );

  const allAgentOptions = useMemo<ComboboxOption[]>(
    () =>
      agents.map((a) => ({
        value: a.id,
        label: a.name,
        description: a.agentType,
      })),
    [agents],
  );

  // Load existing rule for editing
  const loadRule = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const resp = await fetch(`${API}/${id}`);
      const result: ApiResult<AlertRuleDto> = await resp.json();
      if (result.success && result.data) {
        const r = result.data;
        setName(r.name);
        setDescription(r.description ?? "");
        setSeverity(r.severity);
        // Derive route from which IDs are set
        if (r.teamAgentId) {
          setRoute("RootCauseAnalysis");
        } else {
          setRoute("SopExecution");
        }
        setMatchers(r.matchers);
        setCooldownMinutes(r.cooldownMinutes);
        setSopId(r.sopId ?? "");
        setResponderAgentId(r.responderAgentId ?? "");
        setTeamAgentId(r.teamAgentId ?? "");
        setSummarizerAgentId(r.summarizerAgentId ?? "");
        setContextProviders(r.contextProviders ?? []);
      } else {
        setError(result.error ?? "规则不存在");
      }
    } catch {
      setError("加载失败");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    if (isEdit) loadRule();
  }, [isEdit, loadRule]);

  const handleSubmit = async () => {
    setSaving(true);
    setError(null);
    try {
      if (isEdit) {
        const body: UpdateAlertRuleRequest = {
          name,
          description: description || undefined,
          severity,
          matchers,
          cooldownMinutes,
          sopId: sopId || undefined,
          responderAgentId: responderAgentId || undefined,
          teamAgentId: teamAgentId || undefined,
          summarizerAgentId: summarizerAgentId || undefined,
          contextProviders: contextProviders.length > 0 ? contextProviders : undefined,
        };
        const resp = await fetch(`${API}/${id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        });
        const result = await resp.json();
        if (!result.success) {
          setError(result.error ?? "更新失败");
          return;
        }
        navigate(`/alert-rules/${id}`);
      } else {
        const body: CreateAlertRuleRequest = {
          name,
          description: description || undefined,
          severity,
          matchers,
          cooldownMinutes,
          sopId: sopId || undefined,
          responderAgentId: responderAgentId || undefined,
          teamAgentId: teamAgentId || undefined,
          summarizerAgentId: summarizerAgentId || undefined,
          contextProviders: contextProviders.length > 0 ? contextProviders : undefined,
        };
        const resp = await fetch(API, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        });
        const result: ApiResult<AlertRuleDto> = await resp.json();
        if (result.success && result.data) {
          navigate(`/alert-rules/${result.data.id}`);
        } else {
          setError(result.error ?? "创建失败");
        }
      }
    } catch {
      setError("网络错误");
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title={isEdit ? "编辑告警规则" : "创建告警规则"}
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/alert-rules")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
        actions={
          <Button size="sm" onClick={handleSubmit} disabled={saving || !name}>
            {saving ? (
              <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
            ) : (
              <Save className="mr-1 h-3.5 w-3.5" />
            )}
            {isEdit ? "保存" : "创建"}
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        {error && (
          <div className="mb-4 rounded-md bg-destructive/10 p-3 text-sm text-destructive">
            {error}
          </div>
        )}

        <div className="mx-auto max-w-2xl space-y-6">
          {/* Basic info */}
          <div className="space-y-3">
            <div>
              <Label>规则名称 *</Label>
              <Input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="例: CPU 使用率告警"
              />
            </div>
            <div>
              <Label>描述</Label>
              <Textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="规则描述（可选）"
                rows={2}
              />
            </div>
          </div>

          {/* Severity + Route */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <Label>严重级</Label>
              <Select value={severity} onValueChange={setSeverity}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {INCIDENT_SEVERITIES.map((s) => (
                    <SelectItem key={s} value={s}>
                      {s}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label>路由类型</Label>
              <Select value={route} onValueChange={setRoute}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {INCIDENT_ROUTES.map((r) => (
                    <SelectItem key={r} value={r}>
                      {ROUTE_LABELS[r]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          {/* Cooldown */}
          <div>
            <Label>冷却时间（分钟）</Label>
            <Input
              type="number"
              value={cooldownMinutes}
              onChange={(e) => setCooldownMinutes(Number(e.target.value))}
              min={0}
            />
          </div>

          {/* Matchers */}
          <MatcherEditor matchers={matchers} onChange={setMatchers} />

          {/* Agent / SOP Bindings */}
          <div className="space-y-3">
            <h3 className="text-sm font-semibold">Agent / SOP 绑定</h3>
            {route === "SopExecution" && (
              <>
                <div>
                  <Label>SOP 技能</Label>
                  <Combobox
                    options={skillOptions}
                    value={sopId}
                    onChange={setSopId}
                    placeholder="选择 SOP 技能…"
                    searchPlaceholder="搜索技能名称…"
                    emptyText="未找到匹配技能"
                  />
                </div>
                <div>
                  <Label>Responder Agent</Label>
                  <Combobox
                    options={nonTeamAgentOptions}
                    value={responderAgentId}
                    onChange={setResponderAgentId}
                    placeholder="选择 Responder Agent…"
                    searchPlaceholder="搜索 Agent 名称…"
                    emptyText="未找到匹配 Agent"
                  />
                </div>
              </>
            )}
            {route === "RootCauseAnalysis" && (
              <>
                <div>
                  <Label>Team Agent</Label>
                  <Combobox
                    options={teamAgentOptions}
                    value={teamAgentId}
                    onChange={setTeamAgentId}
                    placeholder="选择 Team Agent…"
                    searchPlaceholder="搜索 Team Agent…"
                    emptyText="未找到 Team Agent"
                  />
                </div>
                <div>
                  <Label>Summarizer Agent（可选）</Label>
                  <Combobox
                    options={allAgentOptions}
                    value={summarizerAgentId}
                    onChange={setSummarizerAgentId}
                    placeholder="选择 Summarizer Agent…"
                    searchPlaceholder="搜索 Agent 名称…"
                    emptyText="未找到匹配 Agent"
                  />
                </div>
              </>
            )}
          </div>

          {/* Context Init Providers (Spec 027) */}
          <ContextInitEditor
            value={contextProviders}
            onChange={setContextProviders}
          />
        </div>
      </div>
    </div>
  );
}
