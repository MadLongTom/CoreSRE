import { useState } from "react";
import { Loader2, Play } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { executeWorkflow, ApiError } from "@/lib/api/workflows";

interface ExecuteWorkflowDialogProps {
  workflowId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onExecuted: (executionId: string) => void;
}

export function ExecuteWorkflowDialog({
  workflowId,
  open,
  onOpenChange,
  onExecuted,
}: ExecuteWorkflowDialogProps) {
  const [inputJson, setInputJson] = useState("{}");
  const [executing, setExecuting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleExecute = async () => {
    setError(null);

    // Validate JSON
    let inputData: Record<string, unknown>;
    try {
      inputData = JSON.parse(inputJson);
    } catch {
      setError("输入必须是合法的 JSON");
      return;
    }

    setExecuting(true);
    try {
      const result = await executeWorkflow(workflowId, {
        inputData,
      });
      if (result.success && result.data) {
        onExecuted(result.data.id);
      } else {
        setError(result.message ?? "执行失败");
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message ?? "执行失败，请重试");
      } else {
        setError("执行失败，请重试");
      }
    } finally {
      setExecuting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>执行 Workflow</DialogTitle>
          <DialogDescription>
            提供执行所需的输入数据（JSON 格式）。
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="exec-input">输入数据 (JSON)</Label>
            <Textarea
              id="exec-input"
              value={inputJson}
              onChange={(e) => setInputJson(e.target.value)}
              rows={8}
              className="font-mono text-sm"
              placeholder='{ "key": "value" }'
            />
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={executing}
          >
            取消
          </Button>
          <Button onClick={handleExecute} disabled={executing}>
            {executing ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Play className="mr-2 h-4 w-4" />
            )}
            执行
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
