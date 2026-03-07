import { useCallback, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  CheckCircle,
  XCircle,
  Rocket,
  Archive,
  FlaskConical,
  ShieldCheck,
  Loader2,
  AlertTriangle,
} from "lucide-react";
import type { SkillRegistration, SopValidationResult } from "@/types/skill";
import {
  validateSop,
  approveSop,
  rejectSop,
  publishSop,
  archiveSop,
  dryRunSop,
} from "@/lib/api/skills";

interface Props {
  skill: SkillRegistration;
  onRefresh: () => void;
}

export function SopLifecyclePanel({ skill, onRefresh }: Props) {
  const [acting, setActing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [validation, setValidation] = useState<SopValidationResult | null>(
    skill.validationResult ?? null,
  );

  // Review form
  const [showReviewForm, setShowReviewForm] = useState<"approve" | "reject" | null>(null);
  const [reviewedBy, setReviewedBy] = useState("");
  const [comment, setComment] = useState("");

  const act = useCallback(
    async (fn: () => Promise<unknown>) => {
      setActing(true);
      setError(null);
      try {
        await fn();
        onRefresh();
      } catch (err) {
        setError(err instanceof Error ? err.message : "操作失败");
      } finally {
        setActing(false);
      }
    },
    [onRefresh],
  );

  const handleValidate = () =>
    act(async () => {
      const res = await validateSop(skill.id);
      if (res.data) setValidation(res.data);
    });

  const handleApprove = () =>
    act(async () => {
      await approveSop(skill.id, { reviewedBy, comment: comment || undefined });
      setShowReviewForm(null);
    });

  const handleReject = () =>
    act(async () => {
      await rejectSop(skill.id, { reviewedBy, reason: comment });
      setShowReviewForm(null);
    });

  const handlePublish = () => act(() => publishSop(skill.id));
  const handleArchive = () => act(() => archiveSop(skill.id));
  const handleDryRun = () => act(() => dryRunSop(skill.id));

  const status = skill.status;

  return (
    <div className="space-y-3 rounded-md border p-4">
      <h3 className="text-sm font-semibold">SOP 生命周期管理</h3>

      {error && (
        <div className="rounded-md bg-destructive/10 p-2 text-xs text-destructive">
          {error}
        </div>
      )}

      {/* Validation result */}
      {validation && (
        <div
          className={`rounded-md p-3 text-xs ${
            validation.isValid
              ? "bg-green-50 text-green-800"
              : "bg-red-50 text-red-800"
          }`}
        >
          <div className="flex items-center gap-1 font-medium mb-1">
            {validation.isValid ? (
              <CheckCircle className="h-3.5 w-3.5" />
            ) : (
              <AlertTriangle className="h-3.5 w-3.5" />
            )}
            {validation.isValid ? "校验通过" : "校验未通过"}
          </div>
          {validation.errors.length > 0 && (
            <ul className="ml-4 list-disc">
              {validation.errors.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
            </ul>
          )}
          {validation.warnings.length > 0 && (
            <ul className="ml-4 list-disc text-yellow-700">
              {validation.warnings.map((w, i) => (
                <li key={i}>{w}</li>
              ))}
            </ul>
          )}
          {validation.dangerousSteps.length > 0 && (
            <p className="mt-1">
              ⚠ 危险步骤: {validation.dangerousSteps.join(", ")}
            </p>
          )}
        </div>
      )}

      {/* Action buttons — show based on current status */}
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          variant="outline"
          onClick={handleValidate}
          disabled={acting}
        >
          <ShieldCheck className="mr-1 h-3.5 w-3.5" />
          校验
        </Button>

        {(status === "Draft" || status === "Invalid") && (
          <>
            <Button
              size="sm"
              variant="outline"
              onClick={() => setShowReviewForm("approve")}
              disabled={acting}
            >
              <CheckCircle className="mr-1 h-3.5 w-3.5" />
              审核通过
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => setShowReviewForm("reject")}
              disabled={acting}
            >
              <XCircle className="mr-1 h-3.5 w-3.5" />
              驳回
            </Button>
          </>
        )}

        {status === "Reviewed" && (
          <Button
            size="sm"
            onClick={handlePublish}
            disabled={acting}
          >
            <Rocket className="mr-1 h-3.5 w-3.5" />
            发布
          </Button>
        )}

        {(status === "Active" || status === "Degraded") && (
          <Button
            size="sm"
            variant="secondary"
            onClick={handleArchive}
            disabled={acting}
          >
            <Archive className="mr-1 h-3.5 w-3.5" />
            归档
          </Button>
        )}

        <Button
          size="sm"
          variant="outline"
          onClick={handleDryRun}
          disabled={acting}
        >
          <FlaskConical className="mr-1 h-3.5 w-3.5" />
          试运行
        </Button>

        {acting && <Loader2 className="h-4 w-4 animate-spin self-center" />}
      </div>

      {/* Review form */}
      {showReviewForm && (
        <div className="space-y-2 rounded-md border bg-muted/30 p-3">
          <h4 className="text-xs font-medium">
            {showReviewForm === "approve" ? "审核通过" : "驳回 SOP"}
          </h4>
          <div className="space-y-1">
            <Label className="text-xs">审核人</Label>
            <Input
              className="h-8 text-sm"
              value={reviewedBy}
              onChange={(e) => setReviewedBy(e.target.value)}
              placeholder="您的名字"
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">
              {showReviewForm === "approve" ? "备注（可选）" : "驳回理由"}
            </Label>
            <Textarea
              className="text-sm"
              rows={2}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              placeholder={
                showReviewForm === "approve"
                  ? "审核意见..."
                  : "请输入驳回理由..."
              }
            />
          </div>
          <div className="flex gap-2">
            <Button
              size="sm"
              disabled={
                acting ||
                !reviewedBy ||
                (showReviewForm === "reject" && !comment)
              }
              onClick={
                showReviewForm === "approve" ? handleApprove : handleReject
              }
            >
              确认
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => setShowReviewForm(null)}
            >
              取消
            </Button>
          </div>
        </div>
      )}

      {/* Metadata */}
      {(skill.reviewedBy || skill.version) && (
        <div className="text-xs text-muted-foreground space-y-0.5">
          {skill.version && <p>版本: v{skill.version}</p>}
          {skill.reviewedBy && (
            <p>
              审核人: {skill.reviewedBy}
              {skill.reviewedAt &&
                ` · ${new Date(skill.reviewedAt).toLocaleString()}`}
            </p>
          )}
          {skill.reviewComment && <p>审核意见: {skill.reviewComment}</p>}
        </div>
      )}
    </div>
  );
}
