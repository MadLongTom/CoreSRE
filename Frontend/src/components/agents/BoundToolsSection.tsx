import { useEffect, useState } from "react";
import { Link } from "react-router";
import { AlertTriangle, ExternalLink, Wrench } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { getAvailableFunctions } from "@/lib/api/tools";
import type { BindableTool } from "@/types/tool";

interface BoundToolsSectionProps {
  /** Tool reference IDs (from LlmConfig.toolRefs) */
  toolRefs: string[];
}

/**
 * Agent 详情页绑定工具概览 — 只读卡片列表。
 *
 * - 从 /api/tools/available-functions 获取工具详情
 * - 已删除的 toolRef 显示灰色占位卡片
 * - 每张卡片可点击导航到工具详情页
 */
export default function BoundToolsSection({ toolRefs }: BoundToolsSectionProps) {
  const [tools, setTools] = useState<Map<string, BindableTool>>(new Map());
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (toolRefs.length === 0) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    getAvailableFunctions({ status: "all" })
      .then((result) => {
        if (!cancelled && result.data) {
          const map = new Map<string, BindableTool>();
          for (const t of result.data) map.set(t.id, t);
          setTools(map);
        }
      })
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [toolRefs]);

  if (toolRefs.length === 0) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Wrench className="h-4 w-4" />
          绑定工具 ({toolRefs.length})
        </CardTitle>
      </CardHeader>
      <CardContent>
        {loading ? (
          <p className="text-sm text-muted-foreground">加载中...</p>
        ) : (
          <div className="grid gap-2">
            {toolRefs.map((refId) => {
              const tool = tools.get(refId);
              return tool ? (
                <ToolCard key={refId} tool={tool} />
              ) : (
                <DeletedToolCard key={refId} refId={refId} />
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ToolCard({ tool }: { tool: BindableTool }) {
  const toolDetailPath =
    tool.toolType === "McpTool" ? undefined : `/tools/${tool.id}`;

  const content = (
    <div className="flex items-center justify-between rounded-md border p-3 hover:bg-accent/50 transition-colors">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium truncate">{tool.name}</span>
          <Badge
            variant="outline"
            className={
              tool.toolType === "RestApi"
                ? "text-[10px] px-1 py-0 text-blue-600 border-blue-300"
                : "text-[10px] px-1 py-0 text-purple-600 border-purple-300"
            }
          >
            {tool.toolType === "RestApi" ? "REST API" : "MCP Tool"}
          </Badge>
          {tool.status === "Inactive" && (
            <Badge
              variant="outline"
              className="gap-0.5 text-[10px] px-1 py-0 text-amber-500 border-amber-300"
            >
              <AlertTriangle className="h-2.5 w-2.5" />
              Inactive
            </Badge>
          )}
        </div>
        {tool.description && (
          <p className="mt-0.5 text-xs text-muted-foreground truncate">
            {tool.description}
          </p>
        )}
        {tool.parentName && (
          <p className="text-xs text-muted-foreground">
            服务器: {tool.parentName}
          </p>
        )}
      </div>
      {toolDetailPath && (
        <ExternalLink className="h-4 w-4 shrink-0 text-muted-foreground" />
      )}
    </div>
  );

  if (toolDetailPath) {
    return (
      <Link to={toolDetailPath} className="block">
        {content}
      </Link>
    );
  }

  return content;
}

function DeletedToolCard({ refId }: { refId: string }) {
  return (
    <div className="flex items-center rounded-md border border-dashed p-3 opacity-50">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium text-muted-foreground">
            工具已删除
          </span>
          <Badge variant="outline" className="text-[10px] px-1 py-0">
            已移除
          </Badge>
        </div>
        <p className="mt-0.5 text-xs text-muted-foreground font-mono">
          {refId}
        </p>
      </div>
    </div>
  );
}
