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
import { deleteProvider, ApiError } from "@/lib/api/providers";

interface DeleteProviderDialogProps {
  providerId: string;
  providerName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onDeleted: () => void;
}

export function DeleteProviderDialog({
  providerId,
  providerName,
  open,
  onOpenChange,
  onDeleted,
}: DeleteProviderDialogProps) {
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDelete = async () => {
    setDeleting(true);
    setError(null);
    try {
      await deleteProvider(providerId);
      onDeleted();
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setError(apiErr.message ?? "无法删除：有 Agent 正在引用此 Provider");
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
          <DialogTitle>确认删除 Provider</DialogTitle>
          <DialogDescription>
            确认删除 Provider &ldquo;{providerName}&rdquo;？此操作不可恢复。
          </DialogDescription>
        </DialogHeader>

        {error && (
          <p className="text-sm text-destructive">{error}</p>
        )}

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
