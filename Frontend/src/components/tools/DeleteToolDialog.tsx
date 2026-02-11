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
import { deleteTool, ApiError } from "@/lib/api/tools";

interface DeleteToolDialogProps {
  toolId: string;
  toolName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onDeleted: () => void;
}

export function DeleteToolDialog({
  toolId,
  toolName,
  open,
  onOpenChange,
  onDeleted,
}: DeleteToolDialogProps) {
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDelete = async () => {
    setDeleting(true);
    setError(null);
    try {
      await deleteTool(toolId);
      onDeleted();
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setError(apiErr.message ?? "无法删除：有 Agent 正在引用此工具");
      } else {
        setError(apiErr.message ?? "删除失败，请重试");
      }
    } finally {
      setDeleting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>确认删除工具</DialogTitle>
          <DialogDescription>
            确认删除工具 &ldquo;{toolName}&rdquo;？此操作不可恢复。
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
