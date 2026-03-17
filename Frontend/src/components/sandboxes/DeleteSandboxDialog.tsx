import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import type { SandboxInstance } from "@/types/sandbox";

interface Props {
  sandbox: SandboxInstance | null;
  onConfirm: () => void;
  onCancel: () => void;
}

export function DeleteSandboxDialog({ sandbox, onConfirm, onCancel }: Props) {
  return (
    <AlertDialog open={!!sandbox} onOpenChange={(open) => !open && onCancel()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>删除沙箱</AlertDialogTitle>
          <AlertDialogDescription>
            确定要删除沙箱 <strong>{sandbox?.name}</strong> 吗？此操作不可撤销，所有关联的工作区数据将被永久清除。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel onClick={onCancel}>取消</AlertDialogCancel>
          <AlertDialogAction onClick={onConfirm} className="bg-destructive text-destructive-foreground hover:bg-destructive/90">
            删除
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
