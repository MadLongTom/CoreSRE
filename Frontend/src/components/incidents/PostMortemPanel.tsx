import { useCallback, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Loader2, FileCheck } from "lucide-react";
import { annotatePostMortem } from "@/lib/api/incidents";
import type {
  PostMortemAnnotation,
  RcaAccuracyRating,
  SopEffectivenessRating,
  IncidentRoute,
} from "@/types/incident";

const RCA_RATINGS: { value: RcaAccuracyRating; label: string }[] = [
  { value: "Accurate", label: "准确" },
  { value: "PartiallyAccurate", label: "部分准确" },
  { value: "Inaccurate", label: "不准确" },
  { value: "NotApplicable", label: "不适用" },
];

const SOP_RATINGS: { value: SopEffectivenessRating; label: string }[] = [
  { value: "Effective", label: "有效" },
  { value: "PartiallyEffective", label: "部分有效" },
  { value: "Ineffective", label: "无效" },
];

interface Props {
  incidentId: string;
  route: IncidentRoute;
  postMortem: PostMortemAnnotation | null;
  onRefresh: () => void;
}

export function PostMortemPanel({ incidentId, route, postMortem, onRefresh }: Props) {
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [actualRootCause, setActualRootCause] = useState("");
  const [rcaAccuracy, setRcaAccuracy] = useState<RcaAccuracyRating>("Accurate");
  const [sopEffectiveness, setSopEffectiveness] = useState<SopEffectivenessRating | "">("");
  const [improvementNotes, setImprovementNotes] = useState("");
  const [annotatedBy, setAnnotatedBy] = useState("");

  const handleSave = useCallback(async () => {
    setSaving(true);
    setError(null);
    try {
      await annotatePostMortem(incidentId, {
        actualRootCause,
        rcaAccuracy,
        sopEffectiveness: sopEffectiveness || undefined,
        improvementNotes: improvementNotes || undefined,
        annotatedBy,
      });
      setEditing(false);
      onRefresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "保存失败");
    } finally {
      setSaving(false);
    }
  }, [incidentId, actualRootCause, rcaAccuracy, sopEffectiveness, improvementNotes, annotatedBy, onRefresh]);

  // Read-only view
  if (postMortem && !editing) {
    return (
      <div className="space-y-2 rounded-md border p-3">
        <div className="flex items-center justify-between">
          <h4 className="text-xs font-semibold flex items-center gap-1">
            <FileCheck className="h-3.5 w-3.5" />
            Post-mortem 标注
          </h4>
          <span className="text-xs text-muted-foreground">
            {postMortem.annotatedBy} · {new Date(postMortem.annotatedAt).toLocaleString()}
          </span>
        </div>
        <div className="grid grid-cols-2 gap-2 text-xs">
          <div>
            <span className="text-muted-foreground">实际根因:</span>
            <p className="mt-0.5">{postMortem.actualRootCause}</p>
          </div>
          <div className="space-y-1">
            <div className="flex items-center gap-1">
              <span className="text-muted-foreground">RCA 准确性:</span>
              <Badge variant="outline" className="text-xs">
                {RCA_RATINGS.find((r) => r.value === postMortem.rcaAccuracy)?.label ?? postMortem.rcaAccuracy}
              </Badge>
            </div>
            {postMortem.sopEffectiveness && (
              <div className="flex items-center gap-1">
                <span className="text-muted-foreground">SOP 有效性:</span>
                <Badge variant="outline" className="text-xs">
                  {SOP_RATINGS.find((r) => r.value === postMortem.sopEffectiveness)?.label ?? postMortem.sopEffectiveness}
                </Badge>
              </div>
            )}
          </div>
        </div>
        {postMortem.improvementNotes && (
          <p className="text-xs text-muted-foreground">
            改进建议: {postMortem.improvementNotes}
          </p>
        )}
      </div>
    );
  }

  // Edit/create form
  if (!editing && !postMortem) {
    return (
      <Button size="sm" variant="outline" onClick={() => setEditing(true)} className="w-full">
        <FileCheck className="mr-1 h-3.5 w-3.5" />
        添加 Post-mortem 标注
      </Button>
    );
  }

  return (
    <div className="space-y-3 rounded-md border p-3">
      <h4 className="text-xs font-semibold">Post-mortem 标注</h4>

      {error && (
        <div className="rounded bg-destructive/10 p-2 text-xs text-destructive">{error}</div>
      )}

      <div className="space-y-1">
        <Label className="text-xs">实际根因</Label>
        <Textarea
          rows={2}
          className="text-sm"
          value={actualRootCause}
          onChange={(e) => setActualRootCause(e.target.value)}
          placeholder="描述实际根因..."
        />
      </div>

      <div className="space-y-1">
        <Label className="text-xs">RCA 准确性评级</Label>
        <div className="flex gap-2">
          {RCA_RATINGS.map((r) => (
            <Button
              key={r.value}
              size="sm"
              variant={rcaAccuracy === r.value ? "default" : "outline"}
              onClick={() => setRcaAccuracy(r.value)}
              className="text-xs"
            >
              {r.label}
            </Button>
          ))}
        </div>
      </div>

      {(route === "SopExecution" || route === "FallbackRca") && (
        <div className="space-y-1">
          <Label className="text-xs">SOP 有效性评级</Label>
          <div className="flex gap-2">
            {SOP_RATINGS.map((r) => (
              <Button
                key={r.value}
                size="sm"
                variant={sopEffectiveness === r.value ? "default" : "outline"}
                onClick={() => setSopEffectiveness(r.value)}
                className="text-xs"
              >
                {r.label}
              </Button>
            ))}
          </div>
        </div>
      )}

      <div className="space-y-1">
        <Label className="text-xs">改进建议（可选）</Label>
        <Textarea
          rows={2}
          className="text-sm"
          value={improvementNotes}
          onChange={(e) => setImprovementNotes(e.target.value)}
        />
      </div>

      <div className="space-y-1">
        <Label className="text-xs">标注人</Label>
        <Input
          className="h-8 text-sm"
          value={annotatedBy}
          onChange={(e) => setAnnotatedBy(e.target.value)}
          placeholder="您的名字"
        />
      </div>

      <div className="flex gap-2">
        <Button
          size="sm"
          disabled={saving || !actualRootCause || !annotatedBy}
          onClick={handleSave}
        >
          {saving && <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />}
          保存
        </Button>
        <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
          取消
        </Button>
      </div>
    </div>
  );
}
