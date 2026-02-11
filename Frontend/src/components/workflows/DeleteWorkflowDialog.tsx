import { useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { deleteWorkflow, ApiError } from "@/lib/api/workflows";

interface DeleteWorkflowDialogProps {
  workflowId: string;
  workflowName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onDeleted: () => void;
}

export function DeleteWorkflowDialog({
  workflowId,
  workflowName,
  open,
  onOpenChange,
  onDeleted,
}: DeleteWorkflowDialogProps) {
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDelete = async () => {
    setDeleting(true);
    setError(null);
    try {
      await deleteWorkflow(workflowId);
      onDeleted();
    } catch (err) {
      if (err instanceof ApiError) {
        // 409 — workflow is published
        setError(err.message ?? "删除失败，请重试");
      } else {
        setError("删除失败，请重试");
      }
    } finally {
      setDeleting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>确认删除 Workflow</DialogTitle>
          <DialogDescription>
            确认删除 Workflow &ldquo;{workflowName}&rdquo;？此操作不可恢复。
          </DialogDescription>
        </DialogHeader>

        {error && <p className="text-sm text-destructive">{error}</p>}

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={deleting}
          >
            取消
          </Button>
          <Button
            variant="destructive"
            onClick={handleDelete}
            disabled={deleting}
          >
            {deleting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            确认删除
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
