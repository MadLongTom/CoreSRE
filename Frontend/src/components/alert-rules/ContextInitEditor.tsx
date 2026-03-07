import { useCallback, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import {
  Loader2,
  Plus,
  Trash2,
  Play,
  CheckCircle2,
  XCircle,
} from "lucide-react";
import {
  CONTEXT_INIT_CATEGORIES,
  type ContextInitItem,
  type ContextInitResult,
} from "@/types/alert-rule";
import { previewContext } from "@/lib/api/alert-rules";

interface ContextInitEditorProps {
  value: ContextInitItem[];
  onChange: (items: ContextInitItem[]) => void;
}

const EMPTY_ITEM: ContextInitItem = {
  category: "Metrics",
  expression: "",
  label: "",
  lookback: "1h",
};

const CATEGORY_LABELS: Record<string, string> = {
  Metrics: "📊 指标",
  Logs: "📋 日志",
  Tracing: "🔍 链路追踪",
  Alerting: "🔔 告警",
  Deployment: "🚀 部署",
  Git: "📂 代码",
};

export function ContextInitEditor({ value, onChange }: ContextInitEditorProps) {
  const [previewing, setPreviewing] = useState(false);
  const [previewResult, setPreviewResult] = useState<ContextInitResult | null>(
    null,
  );
  const [previewError, setPreviewError] = useState<string | null>(null);

  const addItem = useCallback(() => {
    onChange([...value, { ...EMPTY_ITEM }]);
  }, [value, onChange]);

  const removeItem = useCallback(
    (index: number) => {
      onChange(value.filter((_, i) => i !== index));
      setPreviewResult(null);
    },
    [value, onChange],
  );

  const updateItem = useCallback(
    (index: number, patch: Partial<ContextInitItem>) => {
      onChange(value.map((item, i) => (i === index ? { ...item, ...patch } : item)));
      setPreviewResult(null);
    },
    [value, onChange],
  );

  const handlePreview = useCallback(async () => {
    if (value.length === 0) return;
    setPreviewing(true);
    setPreviewError(null);
    setPreviewResult(null);
    try {
      const result = await previewContext({
        items: value.filter((i) => i.expression.trim()),
      });
      if (result.success && result.data) {
        setPreviewResult(result.data);
      } else {
        setPreviewError(result.message ?? "预览失败");
      }
    } catch (err) {
      setPreviewError(err instanceof Error ? err.message : "预览请求失败");
    } finally {
      setPreviewing(false);
    }
  }, [value]);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">初始化上下文（Spec 027）</h3>
        <div className="flex gap-2">
          {value.length > 0 && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handlePreview}
              disabled={previewing || value.every((i) => !i.expression.trim())}
            >
              {previewing ? (
                <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
              ) : (
                <Play className="mr-1 h-3.5 w-3.5" />
              )}
              预览查询
            </Button>
          )}
          <Button type="button" variant="outline" size="sm" onClick={addItem}>
            <Plus className="mr-1 h-3.5 w-3.5" />
            添加条目
          </Button>
        </div>
      </div>

      <p className="text-xs text-muted-foreground">
        配置告警触发时自动预查询的数据。使用 {"${label}"} 语法引用告警标签值。
      </p>

      {value.length === 0 ? (
        <p className="text-xs text-muted-foreground italic">
          暂无上下文初始化条目。点击"添加条目"来配置。
        </p>
      ) : (
        <div className="space-y-3">
          {value.map((item, index) => (
            <div
              key={index}
              className="rounded-md border p-3 space-y-2 relative"
            >
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="absolute right-1 top-1 h-7 w-7"
                onClick={() => removeItem(index)}
              >
                <Trash2 className="h-3.5 w-3.5 text-muted-foreground" />
              </Button>

              <div className="grid grid-cols-[120px_1fr] gap-2 pr-8">
                <div>
                  <Label className="text-xs">类别</Label>
                  <Select
                    value={item.category}
                    onValueChange={(v) => updateItem(index, { category: v })}
                  >
                    <SelectTrigger className="h-8 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {CONTEXT_INIT_CATEGORIES.map((c) => (
                        <SelectItem key={c} value={c}>
                          {CATEGORY_LABELS[c] ?? c}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div>
                  <Label className="text-xs">标签名称</Label>
                  <Input
                    className="h-8 text-xs"
                    value={item.label ?? ""}
                    onChange={(e) => updateItem(index, { label: e.target.value })}
                    placeholder="例: 服务健康状态"
                  />
                </div>
              </div>

              <div>
                <Label className="text-xs">查询表达式</Label>
                <Input
                  className="h-8 text-xs font-mono"
                  value={item.expression}
                  onChange={(e) =>
                    updateItem(index, { expression: e.target.value })
                  }
                  placeholder='例: up{namespace="${namespace}"}'
                />
              </div>

              <div className="grid grid-cols-2 gap-2">
                <div>
                  <Label className="text-xs">回溯时间</Label>
                  <Input
                    className="h-8 text-xs"
                    value={item.lookback ?? "1h"}
                    onChange={(e) =>
                      updateItem(index, { lookback: e.target.value })
                    }
                    placeholder="1h / 30m / 2d"
                  />
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Preview results */}
      {previewError && (
        <div className="rounded-md bg-destructive/10 p-3 text-xs text-destructive">
          {previewError}
        </div>
      )}

      {previewResult && (
        <div className="rounded-md border p-3 space-y-2">
          <div className="flex items-center gap-2 text-xs font-semibold text-muted-foreground">
            预览结果
            <Badge variant="outline" className="text-[10px]">
              {previewResult.entries.filter((e) => e.success).length}/
              {previewResult.entries.length} 成功
            </Badge>
          </div>
          {previewResult.entries.map((entry, idx) => (
            <div
              key={idx}
              className="rounded-sm border p-2 text-xs space-y-1"
            >
              <div className="flex items-center gap-1.5">
                {entry.success ? (
                  <CheckCircle2 className="h-3.5 w-3.5 text-green-500" />
                ) : (
                  <XCircle className="h-3.5 w-3.5 text-destructive" />
                )}
                <span className="font-medium">{entry.label}</span>
                <Badge variant="outline" className="text-[10px] px-1">
                  {entry.category}
                </Badge>
              </div>
              {entry.success && entry.result && (
                <pre className="max-h-32 overflow-auto rounded bg-muted p-2 text-[11px] font-mono whitespace-pre-wrap">
                  {entry.result.length > 2000
                    ? entry.result.slice(0, 2000) + "\n... [truncated]"
                    : entry.result}
                </pre>
              )}
              {!entry.success && entry.errorMessage && (
                <p className="text-destructive">{entry.errorMessage}</p>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
