import { useCallback, useEffect, useMemo, useState } from "react";
import { Check, ChevronsUpDown, Database, Search, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getDataSources } from "@/lib/api/datasources";
import type { DataSourceRegistration } from "@/types/datasource";
import type { DataSourceRef } from "@/types/agent";

// Category display labels
const CATEGORY_LABELS: Record<string, string> = {
  Metrics: "指标监控",
  Logs: "日志",
  Tracing: "链路追踪",
  Alerting: "告警",
  Deployment: "部署",
  Git: "代码仓库",
};

// Category icons (emoji for simplicity)
const CATEGORY_ICONS: Record<string, string> = {
  Metrics: "📊",
  Logs: "📋",
  Tracing: "🔍",
  Alerting: "🔔",
  Deployment: "🚀",
  Git: "📂",
};

interface DataSourceRefsPickerProps {
  /** Current datasource bindings */
  value: DataSourceRef[];
  /** Called when bindings change */
  onChange: (refs: DataSourceRef[]) => void;
}

/**
 * 数据源绑定选择器 — 可搜索的多选组件，支持函数级别粒度控制。
 *
 * Features:
 * - 从 /api/datasources 加载可用数据源
 * - 按类别分组（Metrics / Logs / Tracing / Alerting / Deployment / Git）
 * - 选中后展示可用函数列表，支持切换全部/部分函数
 * - 已选数据源以可移除的 Badge 展示
 */
export default function DataSourceRefsPicker({
  value,
  onChange,
}: DataSourceRefsPickerProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [dataSources, setDataSources] = useState<DataSourceRegistration[]>([]);
  const [loading, setLoading] = useState(false);
  // Track which datasource's function panel is expanded
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // Load datasources on mount
  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getDataSources({ pageSize: 200 })
      .then((result) => {
        if (!cancelled && result.data) {
          setDataSources(result.data.items);
        }
      })
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Build lookup maps
  const dsMap = useMemo(() => {
    const map = new Map<string, DataSourceRegistration>();
    for (const ds of dataSources) map.set(ds.id, ds);
    return map;
  }, [dataSources]);

  const selectedMap = useMemo(() => {
    const map = new Map<string, DataSourceRef>();
    for (const ref of value) map.set(ref.dataSourceId, ref);
    return map;
  }, [value]);

  // Filter by search
  const filtered = useMemo(() => {
    if (!search.trim()) return dataSources;
    const q = search.toLowerCase();
    return dataSources.filter(
      (ds) =>
        ds.name.toLowerCase().includes(q) ||
        ds.product.toLowerCase().includes(q) ||
        ds.category.toLowerCase().includes(q) ||
        (ds.description?.toLowerCase().includes(q)),
    );
  }, [dataSources, search]);

  // Group by category
  const categoryGroups = useMemo(() => {
    const groups = new Map<string, DataSourceRegistration[]>();
    for (const ds of filtered) {
      const cat = ds.category;
      const list = groups.get(cat) ?? [];
      list.push(ds);
      groups.set(cat, list);
    }
    return groups;
  }, [filtered]);

  const toggleDataSource = useCallback(
    (dsId: string) => {
      if (selectedMap.has(dsId)) {
        // Remove
        onChange(value.filter((r) => r.dataSourceId !== dsId));
        if (expandedId === dsId) setExpandedId(null);
      } else {
        // Add with all functions enabled (null = all)
        onChange([...value, { dataSourceId: dsId, enabledFunctions: null }]);
      }
    },
    [value, selectedMap, onChange, expandedId],
  );

  const removeDataSource = useCallback(
    (dsId: string) => {
      onChange(value.filter((r) => r.dataSourceId !== dsId));
      if (expandedId === dsId) setExpandedId(null);
    },
    [value, onChange, expandedId],
  );

  // Toggle a specific function for a datasource
  const toggleFunction = useCallback(
    (dsId: string, funcName: string, allFunctions: string[]) => {
      const ref = selectedMap.get(dsId);
      if (!ref) return;

      // Current enabled: null means all
      const currentEnabled = ref.enabledFunctions ?? [...allFunctions];
      const isEnabled = currentEnabled.includes(funcName);

      let newEnabled: string[];
      if (isEnabled) {
        newEnabled = currentEnabled.filter((f) => f !== funcName);
      } else {
        newEnabled = [...currentEnabled, funcName];
      }

      // If all functions are enabled, set to null (= all)
      const updatedRef: DataSourceRef =
        newEnabled.length === allFunctions.length
          ? { dataSourceId: dsId, enabledFunctions: null }
          : { dataSourceId: dsId, enabledFunctions: newEnabled };

      onChange(value.map((r) => (r.dataSourceId === dsId ? updatedRef : r)));
    },
    [value, selectedMap, onChange],
  );

  // Toggle all functions for a datasource
  const toggleAllFunctions = useCallback(
    (dsId: string, allFunctions: string[]) => {
      const ref = selectedMap.get(dsId);
      if (!ref) return;

      const currentEnabled = ref.enabledFunctions;
      // If all enabled (null or full list) → disable all; otherwise → enable all
      const allEnabled =
        currentEnabled === null ||
        currentEnabled === undefined ||
        currentEnabled.length === allFunctions.length;

      const updatedRef: DataSourceRef = allEnabled
        ? { dataSourceId: dsId, enabledFunctions: [] }
        : { dataSourceId: dsId, enabledFunctions: null };

      onChange(value.map((r) => (r.dataSourceId === dsId ? updatedRef : r)));
    },
    [value, selectedMap, onChange],
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
            ? `已绑定 ${value.length} 个数据源`
            : "选择数据源..."}
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
              placeholder="搜索数据源..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="border-0 shadow-none focus-visible:ring-0 h-9"
            />
          </div>

          {/* DataSource list */}
          <div className="max-h-72 overflow-y-auto p-1">
            {loading ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                加载中...
              </p>
            ) : filtered.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                {dataSources.length === 0
                  ? "暂无可用数据源，请先注册数据源"
                  : "未找到匹配的数据源"}
              </p>
            ) : (
              [...categoryGroups.entries()].map(([category, dsList]) => (
                <div key={category}>
                  <p className="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                    {CATEGORY_ICONS[category] ?? "📡"}{" "}
                    {CATEGORY_LABELS[category] ?? category}
                  </p>
                  {dsList.map((ds) => {
                    const isSelected = selectedMap.has(ds.id);
                    const ref = selectedMap.get(ds.id);
                    const functions = ds.metadata?.availableFunctions ?? [];
                    const isExpanded = expandedId === ds.id;

                    return (
                      <div key={ds.id}>
                        <button
                          type="button"
                          className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer"
                          onClick={() => toggleDataSource(ds.id)}
                        >
                          <div className="flex h-4 w-4 items-center justify-center">
                            {isSelected && (
                              <Check className="h-4 w-4 text-primary" />
                            )}
                          </div>
                          <Database className="h-3.5 w-3.5 text-muted-foreground" />
                          <div className="flex-1 text-left truncate">
                            <span className="font-medium">{ds.name}</span>
                            <span className="ml-1.5 text-xs text-muted-foreground">
                              {ds.product}
                            </span>
                          </div>
                          {functions.length > 0 && (
                            <Badge
                              variant="outline"
                              className="text-[10px] px-1 py-0"
                            >
                              {ref?.enabledFunctions
                                ? `${ref.enabledFunctions.length}/${functions.length}`
                                : `${functions.length}`}{" "}
                              函数
                            </Badge>
                          )}
                          {ds.status !== "Connected" && (
                            <Badge
                              variant="outline"
                              className="text-[10px] px-1 py-0 text-amber-500 border-amber-300"
                            >
                              {ds.status}
                            </Badge>
                          )}
                        </button>

                        {/* Function-level control for selected datasources */}
                        {isSelected && functions.length > 0 && (
                          <div className="pl-8 pr-2 pb-1">
                            <button
                              type="button"
                              className="text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                              onClick={(e) => {
                                e.stopPropagation();
                                setExpandedId(isExpanded ? null : ds.id);
                              }}
                            >
                              {isExpanded
                                ? "▾ 收起函数列表"
                                : "▸ 展开函数列表（可选择性启用）"}
                            </button>
                            {isExpanded && (
                              <div className="mt-1 space-y-0.5">
                                {/* Select all / deselect all */}
                                <button
                                  type="button"
                                  className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer px-1 py-0.5"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    toggleAllFunctions(ds.id, functions);
                                  }}
                                >
                                  <div className="flex h-3.5 w-3.5 items-center justify-center">
                                    {(!ref?.enabledFunctions ||
                                      ref.enabledFunctions.length ===
                                        functions.length) && (
                                      <Check className="h-3.5 w-3.5 text-primary" />
                                    )}
                                  </div>
                                  全选 / 取消全选
                                </button>
                                {functions.map((fn) => {
                                  const enabled =
                                    !ref?.enabledFunctions ||
                                    ref.enabledFunctions.includes(fn);
                                  return (
                                    <button
                                      key={fn}
                                      type="button"
                                      className="flex w-full items-center gap-1.5 rounded-sm px-1 py-0.5 text-xs hover:bg-accent/50 cursor-pointer"
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        toggleFunction(ds.id, fn, functions);
                                      }}
                                    >
                                      <div className="flex h-3.5 w-3.5 items-center justify-center">
                                        {enabled && (
                                          <Check className="h-3.5 w-3.5 text-primary" />
                                        )}
                                      </div>
                                      <code className="text-[11px] font-mono">
                                        {fn}
                                      </code>
                                    </button>
                                  );
                                })}
                              </div>
                            )}
                          </div>
                        )}

                        {/* Enable mutations toggle (Spec 026) — for Deployment/Git datasources */}
                        {isSelected &&
                          (ds.category === "Deployment" || ds.category === "Git") && (
                            <div className="pl-8 pr-2 pb-1">
                              <button
                                type="button"
                                className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer px-1 py-0.5"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  toggleMutations(ds.id);
                                }}
                              >
                                <div className="flex h-3.5 w-3.5 items-center justify-center">
                                  {ref?.enableMutations && (
                                    <Check className="h-3.5 w-3.5 text-primary" />
                                  )}
                                </div>
                                ⚡ 启用变更操作（重启/扩容/回滚）
                              </button>
                            </div>
                          )}
                      </div>
                    );
                  })}
                </div>
              ))
            )}
          </div>
        </div>
      )}

      {/* Selected badges */}
      {value.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {value.map((ref) => {
            const ds = dsMap.get(ref.dataSourceId);
            const functions = ds?.metadata?.availableFunctions ?? [];
            const enabledCount = ref.enabledFunctions
              ? ref.enabledFunctions.length
              : functions.length;
            return (
              <Badge
                key={ref.dataSourceId}
                variant="secondary"
                className="gap-1 text-xs pr-1"
              >
                <Database className="h-3 w-3" />
                {ds?.name ?? ref.dataSourceId.slice(0, 8) + "…"}
                {functions.length > 0 && ref.enabledFunctions && (
                  <span className="text-muted-foreground ml-0.5">
                    ({enabledCount}/{functions.length})
                  </span>
                )}
                <button
                  type="button"
                  className="ml-0.5 rounded-full hover:bg-muted-foreground/20 p-0.5"
                  onClick={() => removeDataSource(ref.dataSourceId)}
                >
                  <X className="h-3 w-3" />
                </button>
              </Badge>
            );
          })}
        </div>
      )}
    </div>
  );
}
