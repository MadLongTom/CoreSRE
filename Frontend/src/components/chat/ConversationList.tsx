import { useCallback, useState } from "react";
import { MessageSquare, Loader2, Trash2, Users } from "lucide-react";
import { DeleteConversationDialog } from "@/components/chat/DeleteConversationDialog";
import type { ConversationSummary } from "@/types/chat";

interface ConversationListProps {
  conversations: ConversationSummary[];
  selectedId: string | null;
  onSelect: (conversation: ConversationSummary) => void;
  onDelete: (conversationId: string) => Promise<void>;
  loading?: boolean;
}

/**
 * ConversationList — 对话历史列表。
 *
 * 按最近活跃排序展示对话记录，点击可加载历史消息。
 * 展示：Agent 名称、对话标题/最后一条消息预览、时间戳。
 */
export function ConversationList({
  conversations,
  selectedId,
  onSelect,
  onDelete,
  loading = false,
}: ConversationListProps) {
  const [deleteTarget, setDeleteTarget] = useState<ConversationSummary | null>(
    null,
  );
  const formatTime = useCallback((dateStr: string | null) => {
    if (!dateStr) return "";
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);

    if (diffMins < 1) return "刚刚";
    if (diffMins < 60) return `${diffMins} 分钟前`;

    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return `${diffHours} 小时前`;

    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays} 天前`;

    return date.toLocaleDateString("zh-CN", {
      month: "short",
      day: "numeric",
    });
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        加载中…
      </div>
    );
  }

  if (conversations.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 py-8 text-muted-foreground">
        <MessageSquare className="h-8 w-8 opacity-50" />
        <span className="text-sm">暂无对话记录</span>
      </div>
    );
  }

  return (
    <>
      <div className="flex flex-col gap-1">
        {conversations.map((conv) => {
          const isActive = conv.id === selectedId;
          const displayTitle =
            conv.title ?? conv.lastMessage ?? "新对话";
          const preview =
            conv.title && conv.lastMessage ? conv.lastMessage : null;
          const timeStr = formatTime(conv.lastMessageAt ?? conv.createdAt);

          return (
            <div
              key={conv.id}
              className={`group relative flex w-full flex-col gap-0.5 rounded-md px-3 py-2 text-left text-sm transition-colors hover:bg-accent ${
                isActive
                  ? "bg-accent text-accent-foreground"
                  : "text-foreground"
              }`}
            >
              <button
                onClick={() => onSelect(conv)}
                className="flex w-full flex-col gap-0.5 text-left"
              >
                {/* Agent name + type badge + time */}
                <div className="flex items-center justify-between">
                  <span className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
                    {conv.agentType === "Team" && (
                      <Users className="h-3 w-3 text-purple-500" />
                    )}
                    {conv.agentName}
                    {conv.agentType === "A2A" && (
                      <span className="rounded bg-blue-100 px-1 py-0.5 text-[9px] font-medium text-blue-700 dark:bg-blue-900 dark:text-blue-300">
                        A2A
                      </span>
                    )}
                    {conv.agentType === "Team" && (
                      <span className="rounded bg-purple-100 px-1 py-0.5 text-[9px] font-medium text-purple-700 dark:bg-purple-900 dark:text-purple-300">
                        Team
                      </span>
                    )}
                  </span>
                  <span className="text-xs text-muted-foreground">
                    {timeStr}
                  </span>
                </div>

                {/* Title */}
                <span className="truncate font-medium">{displayTitle}</span>

                {/* Preview */}
                {preview && (
                  <span className="truncate text-xs text-muted-foreground">
                    {preview}
                  </span>
                )}
              </button>

              {/* Delete button (visible on hover) */}
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  setDeleteTarget(conv);
                }}
                className="absolute right-2 top-2 rounded p-1 opacity-0 transition-opacity hover:bg-destructive/10 hover:text-destructive group-hover:opacity-100"
                title="删除对话"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          );
        })}
      </div>

      <DeleteConversationDialog
        open={deleteTarget !== null}
        onOpenChange={(open) => {
          if (!open) setDeleteTarget(null);
        }}
        onConfirm={async () => {
          if (deleteTarget) {
            await onDelete(deleteTarget.id);
            setDeleteTarget(null);
          }
        }}
        conversationTitle={deleteTarget?.title}
      />
    </>
  );
}
