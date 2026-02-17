import { useEffect, useState, useCallback } from "react";
import { Link, useNavigate } from "react-router";
import { Plus, RefreshCw, Loader2, Database } from "lucide-react";
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
import { getDataSources, type ApiError } from "@/lib/api/datasources";
import type {
  DataSourceRegistration,
  DataSourceCategory,
  DataSourceStatus,
} from "@/types/datasource";
import {
  DATA_SOURCE_CATEGORIES,
  DATA_SOURCE_STATUSES,
  categoryLabel,
  statusLabel,
  statusVariant,
} from "@/types/datasource";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

export default function DataSourceListPage() {
  const navigate = useNavigate();
  const [dataSources, setDataSources] = useState<DataSourceRegistration[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [categoryFilter, setCategoryFilter] = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [search, setSearch] = useState("");

  const fetchDataSources = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getDataSources({
        category: categoryFilter !== "all" ? categoryFilter : undefined,
        status: statusFilter !== "all" ? statusFilter : undefined,
        search: search.trim() || undefined,
      });
      if (result.success && result.data) {
        setDataSources(result.data.items);
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
  }, [categoryFilter, statusFilter, search]);

  useEffect(() => {
    fetchDataSources();
  }, [fetchDataSources]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="数据源管理"
        actions={
          <Button size="sm" onClick={() => navigate("/datasources/new")}>
            <Plus className="mr-2 h-4 w-4" />
            新建数据源
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Filters */}
        <div className="flex items-center gap-4">
          <Input
            placeholder="搜索数据源名称..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="max-w-xs"
          />
          <Select value={categoryFilter} onValueChange={setCategoryFilter}>
            <SelectTrigger className="w-[160px]">
              <SelectValue placeholder="分类" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部分类</SelectItem>
              {DATA_SOURCE_CATEGORIES.map((c) => (
                <SelectItem key={c} value={c}>
                  {categoryLabel[c]}
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
              {DATA_SOURCE_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>
                  {statusLabel[s]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="ml-auto text-sm text-muted-foreground">
            共 {totalCount} 个数据源
          </div>
        </div>

        {/* Content */}
        {loading ? (
          <div className="flex items-center justify-center py-20">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center gap-4 py-20">
            <p className="text-destructive">{error}</p>
            <Button variant="outline" onClick={fetchDataSources}>
              <RefreshCw className="mr-2 h-4 w-4" />
              重试
            </Button>
          </div>
        ) : dataSources.length === 0 ? (
          <div className="flex flex-col items-center gap-4 py-20 text-muted-foreground">
            <Database className="h-12 w-12" />
            <p>暂无数据源</p>
            <Button
              onClick={() => navigate("/datasources/new")}
              variant="outline"
            >
              <Plus className="mr-2 h-4 w-4" />
              添加第一个数据源
            </Button>
          </div>
        ) : (
          <div className="rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>分类</TableHead>
                  <TableHead>产品</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>端点</TableHead>
                  <TableHead>健康状态</TableHead>
                  <TableHead>创建时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {dataSources.map((ds) => (
                  <TableRow key={ds.id}>
                    <TableCell>
                      <Link
                        to={`/datasources/${ds.id}`}
                        className="font-medium text-primary hover:underline"
                      >
                        {ds.name}
                      </Link>
                      {ds.description && (
                        <p className="text-xs text-muted-foreground mt-0.5 truncate max-w-xs">
                          {ds.description}
                        </p>
                      )}
                    </TableCell>
                    <TableCell>
                      <Badge variant="outline">
                        {categoryLabel[ds.category as DataSourceCategory] ?? ds.category}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <span className="text-sm">{ds.product}</span>
                    </TableCell>
                    <TableCell>
                      <Badge variant={statusVariant[ds.status as DataSourceStatus] ?? "secondary"}>
                        {statusLabel[ds.status as DataSourceStatus] ?? ds.status}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <span className="text-xs text-muted-foreground truncate max-w-[200px] block">
                        {ds.connectionConfig.baseUrl}
                      </span>
                    </TableCell>
                    <TableCell>
                      {ds.healthCheck ? (
                        <Badge variant={ds.healthCheck.isHealthy ? "default" : "destructive"}>
                          {ds.healthCheck.isHealthy ? "健康" : "异常"}
                        </Badge>
                      ) : (
                        <span className="text-xs text-muted-foreground">未检查</span>
                      )}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {formatDate(ds.createdAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>
    </div>
  );
}
