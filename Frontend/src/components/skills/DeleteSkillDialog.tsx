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
import type { SkillRegistration } from "@/types/skill";

interface Props {
  skill: SkillRegistration | null;
  onConfirm: () => void;
  onCancel: () => void;
}

export function DeleteSkillDialog({ skill, onConfirm, onCancel }: Props) {
  return (
    <AlertDialog open={!!skill} onOpenChange={(open) => !open && onCancel()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>删除 Skill</AlertDialogTitle>
          <AlertDialogDescription>
            确定要删除 Skill <strong>{skill?.name}</strong> 吗？
            此操作不可撤销，关联的文件包也将被删除。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel onClick={onCancel}>取消</AlertDialogCancel>
          <AlertDialogAction
            onClick={onConfirm}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            删除
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
