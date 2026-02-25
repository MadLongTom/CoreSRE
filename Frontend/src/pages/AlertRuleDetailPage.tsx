import { useCallback, useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router";
import { PageHeader } from "@/components/layout/PageHeader";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import {
  ArrowLeft,
  Edit,
  Loader2,
  Trash2,
} from "lucide-react";
import type { AlertRuleDto } from "@/types/alert-rule";
import { MATCH_OP_LABELS } from "@/types/alert-rule";
import type { ApiResult } from "@/types/agent";

const API = "/api/alert-rules";

export default function AlertRuleDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [rule, setRule] = useState<AlertRuleDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchRule = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const resp = await fetch(`${API}/${id}`);
      const result: ApiResult<AlertRuleDto> = await resp.json();
      if (result.success && result.data) {
        setRule(result.data);
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
    fetchRule();
  }, [fetchRule]);

  const handleDelete = async () => {
    if (!id || !confirm("确认删除此规则？")) return;
    try {
      const resp = await fetch(`${API}/${id}`, { method: "DELETE" });
      const result = await resp.json();
      if (result.success) {
        navigate("/alert-rules");
      } else {
        setError(result.error ?? "删除失败");
      }
    } catch {
      setError("网络错误");
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error || !rule) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3">
        <p className="text-sm text-destructive">{error ?? "未知错误"}</p>
        <Button variant="outline" onClick={() => navigate("/alert-rules")}>
          返回列表
        </Button>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title={rule.name}
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
          <div className="flex items-center gap-2">
            <Badge variant={rule.status === "Active" ? "default" : "secondary"}>
              {rule.status}
            </Badge>
            <Button
              variant="outline"
              size="sm"
              onClick={() => navigate(`/alert-rules/${id}/edit`)}
            >
              <Edit className="mr-1 h-3.5 w-3.5" />
              编辑
            </Button>
            <Button variant="destructive" size="sm" onClick={handleDelete}>
              <Trash2 className="mr-1 h-3.5 w-3.5" />
              删除
            </Button>
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        <div className="mx-auto max-w-2xl space-y-6">
          {/* Description */}
          {rule.description && (
            <p className="text-sm text-muted-foreground">{rule.description}</p>
          )}

          {/* Properties */}
          <div className="grid grid-cols-2 gap-4">
            <InfoItem label="严重级" value={rule.severity} />
            <InfoItem
              label="路由类型"
              value={rule.teamAgentId ? "根因分析" : "SOP 执行"}
            />
            <InfoItem label="冷却时间" value={`${rule.cooldownMinutes} 分钟`} />
            <InfoItem
              label="创建时间"
              value={new Date(rule.createdAt).toLocaleString()}
            />
          </div>

          <Separator />

          {/* Matchers */}
          <div>
            <h3 className="mb-2 text-sm font-semibold">标签匹配条件</h3>
            {rule.matchers.length === 0 ? (
              <p className="text-xs text-muted-foreground">无匹配条件</p>
            ) : (
              <div className="space-y-1">
                {rule.matchers.map((m, idx) => (
                  <div
                    key={idx}
                    className="flex items-center gap-2 rounded-md bg-muted px-3 py-1.5 text-sm"
                  >
                    <span className="font-mono">{m.label}</span>
                    <span className="text-muted-foreground">
                      {MATCH_OP_LABELS[m.op]}
                    </span>
                    <span className="font-mono">{m.value}</span>
                  </div>
                ))}
              </div>
            )}
          </div>

          <Separator />

          {/* Bindings */}
          <div>
            <h3 className="mb-2 text-sm font-semibold">Agent / SOP 绑定</h3>
            <div className="grid grid-cols-2 gap-4">
              {rule.sopId && <InfoItem label="SOP Skill ID" value={rule.sopId} />}
              {rule.responderAgentId && (
                <InfoItem
                  label="Responder Agent ID"
                  value={rule.responderAgentId}
                />
              )}
              {rule.teamAgentId && (
                <InfoItem label="Team Agent ID" value={rule.teamAgentId} />
              )}
              {rule.summarizerAgentId && (
                <InfoItem
                  label="Summarizer Agent ID"
                  value={rule.summarizerAgentId}
                />
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function InfoItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{value}</dd>
    </div>
  );
}
