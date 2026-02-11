import { useEffect, useState, useCallback } from "react";
import { Link, useNavigate } from "react-router";
import { Plus, RefreshCw, Loader2, Wrench, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { PageHeader } from "@/components/layout/PageHeader";
import { getTools, type ApiError } from "@/lib/api/tools";
import type { ToolRegistration, ToolType, ToolStatus } from "@/types/tool";
import { TOOL_TYPES, TOOL_STATUSES } from "@/types/tool";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

const toolTypeLabel: Record<ToolType, string> = {
  RestApi: "REST API",
  McpServer: "MCP Server",
};

const statusVariant: Record<ToolStatus, "default" | "secondary"> = {
  Active: "default",
  Inactive: "secondary",
};

export default function ToolListPage() {
  const navigate = useNavigate();
  const [tools, setTools] = useState<ToolRegistration[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [toolTypeFilter, setToolTypeFilter] = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [search, setSearch] = useState("");

  const fetchTools = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getTools({
        toolType: toolTypeFilter !== "all" ? toolTypeFilter : undefined,
        status: statusFilter !== "all" ? statusFilter : undefined,
        search: search.trim() || undefined,
      });
      if (result.success && result.data) {
        setTools(result.data.items);
        setTotalCount(result.data.totalCount);
      } else {
        setError(result.message ?? "加载失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "加载失败");
    } finally {
      setLoading(false);
    }
  }, [toolTypeFilter, statusFilter, search]);

  useEffect(() => {
    fetchTools();
  }, [fetchTools]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="工具管理"
        actions={
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => navigate("/tools/import")}
            >
              <Upload className="mr-2 h-4 w-4" />
              导入 OpenAPI
            </Button>
            <Button size="sm" onClick={() => navigate("/tools/new")}>
              <Plus className="mr-2 h-4 w-4" />
              新建工具
            </Button>
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Filters */}
        <div className="flex items-center gap-4">
          <Input
            placeholder="搜索工具名称..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="max-w-xs"
          />
          <Select value={toolTypeFilter} onValueChange={setToolTypeFilter}>
            <SelectTrigger className="w-[140px]">
              <SelectValue placeholder="工具类型" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部类型</SelectItem>
              {TOOL_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {toolTypeLabel[t]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={statusFilter} onValueChange={setStatusFilter}>
            <SelectTrigger className="w-[120px]">
              <SelectValue placeholder="状态" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部状态</SelectItem>
              {TOOL_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>
                  {s}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Content */}
        {loading ? (
          <div className="flex items-center justify-center py-20">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center gap-4 py-20">
            <p className="text-destructive">{error}</p>
            <Button variant="outline" onClick={fetchTools}>
              <RefreshCw className="mr-2 h-4 w-4" />
              重试
            </Button>
          </div>
        ) : tools.length === 0 ? (
          <div className="flex flex-col items-center gap-4 py-20 text-muted-foreground">
            <Wrench className="h-12 w-12" />
            <p>暂无工具</p>
            <Button
              onClick={() => navigate("/tools/new")}
              variant="outline"
            >
              <Plus className="mr-2 h-4 w-4" />
              添加第一个工具
            </Button>
          </div>
        ) : (
          <>
            <div className="rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>名称</TableHead>
                    <TableHead>类型</TableHead>
                    <TableHead>状态</TableHead>
                    <TableHead>传输方式</TableHead>
                    <TableHead>端点</TableHead>
                    <TableHead>创建时间</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {tools.map((tool) => (
                    <TableRow key={tool.id}>
                      <TableCell>
                        <Link
                          to={`/tools/${tool.id}`}
                          className="font-medium text-primary hover:underline"
                        >
                          {tool.name}
                        </Link>
                        {tool.description && (
                          <p className="text-xs text-muted-foreground mt-0.5 truncate max-w-xs">
                            {tool.description}
                          </p>
                        )}
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline">
                          {toolTypeLabel[tool.toolType as ToolType] ?? tool.toolType}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge variant={statusVariant[tool.status as ToolStatus] ?? "secondary"}>
                          {tool.status}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground text-sm">
                        {tool.connectionConfig.transportType}
                        {tool.toolType === "RestApi" && (
                          <span className="ml-1 text-xs">
                            ({tool.connectionConfig.httpMethod})
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="text-muted-foreground text-sm font-mono max-w-xs truncate">
                        {tool.connectionConfig.endpoint}
                      </TableCell>
                      <TableCell className="text-muted-foreground text-sm">
                        {formatDate(tool.createdAt)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <p className="text-sm text-muted-foreground">
              共 {totalCount} 个工具
            </p>
          </>
        )}
      </div>
    </div>
  );
}
