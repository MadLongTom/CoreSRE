import { useCallback, useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Combobox, type ComboboxOption } from "@/components/ui/combobox";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import {
  Loader2,
  Play,
  Square,
  ShieldCheck,
  HeartPulse,
  AlertTriangle,
} from "lucide-react";
import {
  startCanary,
  stopCanary,
  getCanaryReport,
  getAlertRuleHealth,
} from "@/lib/api/alert-rules";
import type { AlertRuleHealth, CanaryReport } from "@/types/alert-rule";
import type { SkillRegistration } from "@/types/skill";
import type { ApiResult } from "@/types/agent";

interface Props {
  alertRuleId: string;
  canaryMode: boolean;
  canarySopId: string | null;
  healthScore: number | null;
  onRefresh: () => void;
}

function scoreColor(score: number): string {
  if (score >= 80) return "text-green-600";
  if (score >= 60) return "text-yellow-600";
  return "text-red-600";
}

export function CanaryHealthPanel({
  alertRuleId,
  canaryMode,
  canarySopId,
  healthScore,
  onRefresh,
}: Props) {
  const [acting, setActing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [newCanarySopId, setNewCanarySopId] = useState("");
  const [report, setReport] = useState<CanaryReport | null>(null);
  const [health, setHealth] = useState<AlertRuleHealth | null>(null);
  const [loadingHealth, setLoadingHealth] = useState(false);
  const [skills, setSkills] = useState<SkillRegistration[]>([]);

  // Fetch skills for combobox
  useEffect(() => {
    fetch("/api/skills?pageSize=200")
      .then((r) => r.json())
      .then((result: ApiResult<{ items: SkillRegistration[]; totalCount: number }>) => {
        if (result.success && result.data?.items) setSkills(result.data.items);
      })
      .catch(() => {});
  }, []);

  const skillOptions = useMemo<ComboboxOption[]>(
    () =>
      skills.map((s) => ({
        value: s.id,
        label: s.name,
        description: s.category,
      })),
    [skills],
  );

  // Fetch health on mount
  useEffect(() => {
    setLoadingHealth(true);
    getAlertRuleHealth(alertRuleId)
      .then((res) => {
        if (res.data) setHealth(res.data);
      })
      .catch(() => {})
      .finally(() => setLoadingHealth(false));
  }, [alertRuleId]);

  // Fetch canary report if in canary mode
  useEffect(() => {
    if (!canaryMode) return;
    getCanaryReport(alertRuleId)
      .then((res) => {
        if (res.data) setReport(res.data);
      })
      .catch(() => {});
  }, [alertRuleId, canaryMode]);

  const handleStartCanary = useCallback(async () => {
    if (!newCanarySopId) return;
    setActing(true);
    setError(null);
    try {
      await startCanary(alertRuleId, { canarySopId: newCanarySopId });
      onRefresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "启动失败");
    } finally {
      setActing(false);
    }
  }, [alertRuleId, newCanarySopId, onRefresh]);

  const handleStopCanary = useCallback(async () => {
    setActing(true);
    setError(null);
    try {
      await stopCanary(alertRuleId);
      setReport(null);
      onRefresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "停止失败");
    } finally {
      setActing(false);
    }
  }, [alertRuleId, onRefresh]);

  return (
    <div className="space-y-4">
      {error && (
        <div className="rounded-md bg-destructive/10 p-2 text-xs text-destructive">
          {error}
        </div>
      )}

      {/* ── Health Score ── */}
      <div className="rounded-md border p-4 space-y-3">
        <div className="flex items-center gap-2">
          <HeartPulse className="h-4 w-4" />
          <h3 className="text-sm font-semibold">健康评分</h3>
          {loadingHealth && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
        </div>

        {healthScore != null && (
          <div className={`text-3xl font-bold ${scoreColor(healthScore)}`}>
            {healthScore}
            <span className="text-sm font-normal text-muted-foreground ml-1">
              / 100
            </span>
          </div>
        )}

        {health && (
          <>
            <div className="space-y-1">
              {health.factors.map((f) => (
                <div
                  key={f.name}
                  className="flex items-center justify-between text-xs"
                >
                  <span>{f.name}</span>
                  <span className="text-muted-foreground">
                    {f.earned}/{f.weight} — {f.detail}
                  </span>
                </div>
              ))}
            </div>

            {health.recommendations.length > 0 && (
              <div className="space-y-0.5">
                <p className="text-xs font-medium flex items-center gap-1">
                  <AlertTriangle className="h-3 w-3 text-yellow-600" />
                  改进建议
                </p>
                <ul className="ml-4 list-disc text-xs text-muted-foreground">
                  {health.recommendations.map((r, i) => (
                    <li key={i}>{r}</li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
      </div>

      <Separator />

      {/* ── Canary Mode ── */}
      <div className="rounded-md border p-4 space-y-3">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4" />
          <h3 className="text-sm font-semibold">金丝雀验证</h3>
          {canaryMode && (
            <Badge variant="default" className="bg-blue-100 text-blue-800">
              进行中
            </Badge>
          )}
        </div>

        {canaryMode ? (
          <>
            <div className="text-xs text-muted-foreground">
              金丝雀 SOP ID: <code className="bg-muted px-1 rounded">{canarySopId}</code>
            </div>

            {report && (
              <div className="space-y-2">
                <div className="grid grid-cols-3 gap-2 text-xs">
                  <div>
                    <span className="text-muted-foreground">测试次数</span>
                    <p className="font-medium">{report.totalResults}</p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">一致率</span>
                    <p className="font-medium">
                      {(report.consistencyRate * 100).toFixed(1)}%
                    </p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">平均 Token 差</span>
                    <p className="font-medium">
                      {report.averageTokenDifference.toFixed(0)}
                    </p>
                  </div>
                </div>

                {report.results.length > 0 && (
                  <div className="max-h-40 overflow-y-auto rounded border">
                    <table className="w-full text-xs">
                      <thead>
                        <tr className="border-b bg-muted/50">
                          <th className="px-2 py-1 text-left">事故</th>
                          <th className="px-2 py-1 text-left">一致</th>
                          <th className="px-2 py-1 text-left">耗时</th>
                          <th className="px-2 py-1 text-left">时间</th>
                        </tr>
                      </thead>
                      <tbody>
                        {report.results.map((r) => (
                          <tr key={r.incidentId} className="border-b last:border-0">
                            <td className="px-2 py-1 font-mono">
                              {r.incidentId.slice(0, 8)}…
                            </td>
                            <td className="px-2 py-1">
                              <Badge
                                variant={r.isConsistent ? "default" : "destructive"}
                                className="text-[10px]"
                              >
                                {r.isConsistent ? "是" : "否"}
                              </Badge>
                            </td>
                            <td className="px-2 py-1">{r.shadowDurationMs}ms</td>
                            <td className="px-2 py-1">
                              {new Date(r.createdAt).toLocaleTimeString()}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}

            <Button
              size="sm"
              variant="destructive"
              onClick={handleStopCanary}
              disabled={acting}
            >
              <Square className="mr-1 h-3.5 w-3.5" />
              停止金丝雀
            </Button>
          </>
        ) : (
          <div className="space-y-2">
            <div className="space-y-1">
              <Label className="text-xs">新 SOP</Label>
              <Combobox
                options={skillOptions}
                value={newCanarySopId}
                onChange={setNewCanarySopId}
                placeholder="选择要测试的 SOP"
                searchPlaceholder="搜索 SOP…"
                emptyText="未找到 SOP"
              />
            </div>
            <Button
              size="sm"
              onClick={handleStartCanary}
              disabled={acting || !newCanarySopId}
            >
              {acting ? (
                <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
              ) : (
                <Play className="mr-1 h-3.5 w-3.5" />
              )}
              启动金丝雀验证
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
