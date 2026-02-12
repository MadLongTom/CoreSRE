import { useCallback, useEffect, useMemo, useState } from "react";
import { Check, ChevronsUpDown, Search, Server, X } from "lucide-react";
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

  // Group MCP tools by server name (parentName)
  const mcpServerGroups = useMemo(() => {
    const mcpTools = filtered.filter((t) => t.toolType === "McpTool");
    const groups = new Map<string, BindableTool[]>();
    for (const t of mcpTools) {
      const server = t.parentName ?? "UnknownServer";
      const list = groups.get(server) ?? [];
      list.push(t);
      groups.set(server, list);
    }
    return groups;
  }, [filtered]);

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

  /** Toggle all tools of a given MCP server at once */
  const toggleServer = useCallback(
    (serverTools: BindableTool[]) => {
      const ids = serverTools.map((t) => t.id);
      const allSelected = ids.every((id) => selectedSet.has(id));
      if (allSelected) {
        // Deselect all from this server
        const removeSet = new Set(ids);
        onChange(value.filter((v) => !removeSet.has(v)));
      } else {
        // Select all from this server (add missing ones)
        const toAdd = ids.filter((id) => !selectedSet.has(id));
        onChange([...value, ...toAdd]);
      }
    },
    [value, selectedSet, onChange],
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
                {mcpServerGroups.size > 0 && (
                  <div>
                    <p className="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                      MCP Tool
                    </p>
                    {[...mcpServerGroups.entries()].map(([serverName, serverTools]) => {
                      const allSelected = serverTools.every((t) => selectedSet.has(t.id));
                      const someSelected = !allSelected && serverTools.some((t) => selectedSet.has(t.id));
                      return (
                        <div key={serverName}>
                          {/* Server group header with select-all toggle */}
                          <button
                            type="button"
                            className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent/60 cursor-pointer"
                            onClick={() => toggleServer(serverTools)}
                          >
                            <div className="flex h-4 w-4 items-center justify-center">
                              {allSelected ? (
                                <Check className="h-4 w-4 text-primary" />
                              ) : someSelected ? (
                                <div className="h-2.5 w-2.5 rounded-sm bg-primary/50" />
                              ) : null}
                            </div>
                            <Server className="h-3.5 w-3.5 text-muted-foreground" />
                            <span className="font-medium text-muted-foreground">{serverName}</span>
                            <Badge variant="outline" className="text-[10px] px-1 py-0 ml-auto">
                              {serverTools.length} 个工具
                            </Badge>
                          </button>
                          {/* Individual tools under this server */}
                          <div className="pl-4">
                            {serverTools.map((t) => (
                              <ToolOption
                                key={t.id}
                                tool={t}
                                selected={selectedSet.has(t.id)}
                                onToggle={toggle}
                                showParent={false}
                              />
                            ))}
                          </div>
                        </div>
                      );
                    })}
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
  showParent = true,
}: {
  tool: BindableTool;
  selected: boolean;
  onToggle: (id: string) => void;
  showParent?: boolean;
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
        {showParent && tool.parentName && (
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
