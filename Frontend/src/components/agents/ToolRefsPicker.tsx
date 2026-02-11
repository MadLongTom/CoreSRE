import { useCallback, useEffect, useMemo, useState } from "react";
import { Check, ChevronsUpDown, Search, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getAvailableFunctions } from "@/lib/api/tools";
import type { BindableTool } from "@/types/tool";

interface ToolRefsPickerProps {
  /** Selected tool IDs (GUIDs) */
  value: string[];
  /** Called when selection changes */
  onChange: (ids: string[]) => void;
}

/**
 * 工具选择器 — 可搜索的多选组件，替代手动输入 GUID。
 *
 * Features:
 * - 从 /api/tools/available-functions 加载可选工具列表
 * - 分组：REST API / MCP Tool
 * - 支持即时搜索过滤
 * - 已选工具以可移除的 Badge 展示
 * - 超过 20 个工具时显示警告
 */
export default function ToolRefsPicker({ value, onChange }: ToolRefsPickerProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [tools, setTools] = useState<BindableTool[]>([]);
  const [loading, setLoading] = useState(false);

  // Load available tools on mount
  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getAvailableFunctions({ status: "all" })
      .then((result) => {
        if (!cancelled && result.data) {
          setTools(result.data);
        }
      })
      .catch(() => {
        // silently fail — user can retry
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const selectedSet = useMemo(() => new Set(value), [value]);

  // Build lookup map for selected tool names
  const toolMap = useMemo(() => {
    const map = new Map<string, BindableTool>();
    for (const t of tools) map.set(t.id, t);
    return map;
  }, [tools]);

  // Filter tools by search
  const filtered = useMemo(() => {
    if (!search.trim()) return tools;
    const q = search.toLowerCase();
    return tools.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        (t.description?.toLowerCase().includes(q)) ||
        (t.parentName?.toLowerCase().includes(q)),
    );
  }, [tools, search]);

  // Group by type
  const restApiTools = useMemo(
    () => filtered.filter((t) => t.toolType === "RestApi"),
    [filtered],
  );
  const mcpTools = useMemo(
    () => filtered.filter((t) => t.toolType === "McpTool"),
    [filtered],
  );

  const toggle = useCallback(
    (id: string) => {
      if (selectedSet.has(id)) {
        onChange(value.filter((v) => v !== id));
      } else {
        onChange([...value, id]);
      }
    },
    [value, selectedSet, onChange],
  );

  const remove = useCallback(
    (id: string) => {
      onChange(value.filter((v) => v !== id));
    },
    [value, onChange],
  );

  return (
    <div className="space-y-2 min-w-0">
      {/* Trigger button */}
      <Button
        type="button"
        variant="outline"
        className="w-full justify-between font-normal"
        onClick={() => setOpen(!open)}
      >
        <span className="text-muted-foreground">
          {value.length > 0
            ? `已选择 ${value.length} 个工具`
            : "选择工具..."}
        </span>
        <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
      </Button>

      {/* Dropdown panel */}
      {open && (
        <div className="rounded-md border bg-popover shadow-md overflow-hidden">
          {/* Search input */}
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <Input
              placeholder="搜索工具..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="border-0 shadow-none focus-visible:ring-0 h-9"
            />
          </div>

          {/* Tool list */}
          <div className="max-h-60 overflow-y-auto p-1">
            {loading ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                加载中...
              </p>
            ) : filtered.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                {tools.length === 0
                  ? "暂无可用工具，请先注册工具"
                  : "未找到匹配的工具"}
              </p>
            ) : (
              <>
                {restApiTools.length > 0 && (
                  <div>
                    <p className="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                      REST API
                    </p>
                    {restApiTools.map((t) => (
                      <ToolOption
                        key={t.id}
                        tool={t}
                        selected={selectedSet.has(t.id)}
                        onToggle={toggle}
                      />
                    ))}
                  </div>
                )}
                {mcpTools.length > 0 && (
                  <div>
                    <p className="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                      MCP Tool
                    </p>
                    {mcpTools.map((t) => (
                      <ToolOption
                        key={t.id}
                        tool={t}
                        selected={selectedSet.has(t.id)}
                        onToggle={toggle}
                      />
                    ))}
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      )}

      {/* Selected badges */}
      {value.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {value.map((id) => {
            const tool = toolMap.get(id);
            return (
              <Badge
                key={id}
                variant="secondary"
                className="gap-1 text-xs pr-1"
              >
                {tool?.name ?? id}
                <button
                  type="button"
                  className="ml-0.5 rounded-full hover:bg-muted-foreground/20 p-0.5"
                  onClick={() => remove(id)}
                >
                  <X className="h-3 w-3" />
                </button>
              </Badge>
            );
          })}
        </div>
      )}

      {/* Warning: too many tools */}
      {value.length > 20 && (
        <p className="text-xs text-amber-600">
          绑定超过 20 个工具可能影响 LLM 上下文，建议精简
        </p>
      )}
    </div>
  );
}

// ── Internal sub-component ──

function ToolOption({
  tool,
  selected,
  onToggle,
}: {
  tool: BindableTool;
  selected: boolean;
  onToggle: (id: string) => void;
}) {
  return (
    <button
      type="button"
      className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer"
      onClick={() => onToggle(tool.id)}
    >
      <div className="flex h-4 w-4 items-center justify-center">
        {selected && <Check className="h-4 w-4 text-primary" />}
      </div>
      <div className="flex-1 text-left truncate">
        <span className="font-medium">{tool.name}</span>
        {tool.parentName && (
          <span className="ml-1 text-xs text-muted-foreground">
            ({tool.parentName})
          </span>
        )}
      </div>
      {tool.description && (
        <span className="max-w-[200px] truncate text-xs text-muted-foreground">
          {tool.description}
        </span>
      )}
      {tool.status === "Inactive" && (
        <Badge variant="outline" className="text-[10px] px-1 py-0 text-amber-500 border-amber-300">
          Inactive
        </Badge>
      )}
    </button>
  );
}
