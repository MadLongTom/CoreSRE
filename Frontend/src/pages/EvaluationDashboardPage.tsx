import { useCallback, useEffect, useState } from "react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import {
  Loader2,
  TrendingUp,
  TrendingDown,
  Shield,
  Users,
  Clock,
  Target,
  BarChart3,
  AlertTriangle,
} from "lucide-react";
import {
  getEvaluationDashboard,
  getSopEffectiveness,
  getFeedbackSummary,
} from "@/lib/api/evaluation";
import type { EvaluationDashboard, SopEffectiveness, FeedbackSummary } from "@/types/evaluation";

function MetricCard({
  label,
  value,
  icon: Icon,
  color = "text-foreground",
  suffix,
}: {
  label: string;
  value: string | number;
  icon: React.ElementType;
  color?: string;
  suffix?: string;
}) {
  return (
    <div className="rounded-lg border bg-card p-4 space-y-1">
      <div className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
        <Icon className={`h-4 w-4 ${color}`} />
        {label}
      </div>
      <div className="text-2xl font-bold">
        {value}
        {suffix && <span className="text-sm font-normal text-muted-foreground ml-0.5">{suffix}</span>}
      </div>
    </div>
  );
}

function pct(value: number): string {
  return `${(value * 100).toFixed(1)}%`;
}

function formatMs(ms: number): string {
  if (ms < 1000) return `${ms.toFixed(0)} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;
  return `${(ms / 60_000).toFixed(1)} min`;
}

export default function EvaluationDashboardPage() {
  const [dashboard, setDashboard] = useState<EvaluationDashboard | null>(null);
  const [sops, setSops] = useState<SopEffectiveness[]>([]);
  const [feedback, setFeedback] = useState<FeedbackSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchAll = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [dashRes, sopRes, fbRes] = await Promise.all([
        getEvaluationDashboard(),
        getSopEffectiveness(),
        getFeedbackSummary(),
      ]);
      if (dashRes.data) setDashboard(dashRes.data);
      if (sopRes.data) setSops(sopRes.data);
      if (fbRes.data) setFeedback(fbRes.data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "加载评估数据失败");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchAll();
  }, [fetchAll]);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3">
        <p className="text-sm text-destructive">{error}</p>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <PageHeader title="评估仪表盘" />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* ── Overview Metrics ── */}
        {dashboard && (
          <>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <MetricCard
                label="事故总数"
                value={dashboard.totalIncidents}
                icon={BarChart3}
              />
              <MetricCard
                label="自动修复率"
                value={pct(dashboard.autoResolveRate)}
                icon={TrendingUp}
                color="text-green-600"
              />
              <MetricCard
                label="平均 MTTR"
                value={formatMs(dashboard.averageMttrMs)}
                icon={Clock}
              />
              <MetricCard
                label="SOP 覆盖率"
                value={pct(dashboard.sopCoverageRate)}
                icon={Shield}
                color="text-blue-600"
              />
            </div>

            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <MetricCard
                label="人工介入率"
                value={pct(dashboard.humanInterventionRate)}
                icon={Users}
                color="text-amber-600"
              />
              <MetricCard
                label="超时率"
                value={pct(dashboard.timeoutRate)}
                icon={AlertTriangle}
                color="text-red-500"
              />
              <MetricCard
                label="RCA 准确率"
                value={
                  dashboard.rcaAccuracyRate != null
                    ? pct(dashboard.rcaAccuracyRate)
                    : "—"
                }
                icon={Target}
                color="text-emerald-600"
              />
              <MetricCard
                label="已标注事故"
                value={dashboard.annotatedIncidentCount}
                icon={BarChart3}
              />
            </div>

            {/* MTTR by severity */}
            {Object.keys(dashboard.mttrBySeverity).length > 0 && (
              <div>
                <h3 className="text-sm font-semibold mb-2">各级别平均 MTTR</h3>
                <div className="grid grid-cols-4 gap-3">
                  {["P1", "P2", "P3", "P4"].map((sev) => (
                    <div
                      key={sev}
                      className="rounded-md border px-3 py-2 text-center"
                    >
                      <div className="text-xs text-muted-foreground">{sev}</div>
                      <div className="text-sm font-medium">
                        {dashboard.mttrBySeverity[sev] != null
                          ? formatMs(dashboard.mttrBySeverity[sev])
                          : "—"}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </>
        )}

        <Separator />

        {/* ── SOP Effectiveness ── */}
        <div>
          <h3 className="text-sm font-semibold mb-3">SOP 效能排名</h3>
          {sops.length === 0 ? (
            <p className="text-sm text-muted-foreground">暂无数据</p>
          ) : (
            <div className="rounded-md border">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b bg-muted/50">
                    <th className="px-4 py-2 text-left font-medium">SOP 名称</th>
                    <th className="px-4 py-2 text-right font-medium">使用次数</th>
                    <th className="px-4 py-2 text-right font-medium">成功率</th>
                    <th className="px-4 py-2 text-right font-medium">平均耗时</th>
                    <th className="px-4 py-2 text-right font-medium">人工介入</th>
                  </tr>
                </thead>
                <tbody>
                  {sops.map((s) => (
                    <tr key={s.sopId} className="border-b last:border-0">
                      <td className="px-4 py-2">{s.sopName}</td>
                      <td className="px-4 py-2 text-right">{s.usageCount}</td>
                      <td className="px-4 py-2 text-right">
                        <Badge
                          variant={
                            s.successRate >= 0.8
                              ? "default"
                              : s.successRate >= 0.5
                                ? "secondary"
                                : "destructive"
                          }
                        >
                          {pct(s.successRate)}
                        </Badge>
                      </td>
                      <td className="px-4 py-2 text-right">
                        {formatMs(s.averageExecutionMs)}
                      </td>
                      <td className="px-4 py-2 text-right">
                        {s.humanInterventionCount}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <Separator />

        {/* ── Feedback Summary (Spec 025) ── */}
        {feedback && (
          <div>
            <h3 className="text-sm font-semibold mb-3">反馈闭环摘要</h3>
            <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
              <MetricCard
                label="降级次数"
                value={feedback.fallbackCount}
                icon={TrendingDown}
                color="text-orange-600"
              />
              <MetricCard
                label="降级率"
                value={pct(feedback.fallbackRate)}
                icon={TrendingDown}
                color="text-orange-600"
              />
              <MetricCard
                label="金丝雀一致率"
                value={pct(feedback.canaryConsistencyRate)}
                icon={Shield}
                color="text-blue-600"
              />
              <MetricCard
                label="SOP 自动禁用"
                value={feedback.sopsAutoDisabledCount}
                icon={AlertTriangle}
                color="text-red-500"
              />
              <MetricCard
                label="效能下降"
                value={feedback.sopsDegradedCount}
                icon={TrendingDown}
                color="text-amber-600"
              />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
