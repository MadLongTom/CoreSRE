import { useEffect, useState, useCallback } from "react";
import { Link, useNavigate } from "react-router";
import { Plus, RefreshCw, Loader2, Server } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/PageHeader";
import { getProviders, type ApiError } from "@/lib/api/providers";
import type { LlmProviderSummary } from "@/types/provider";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

export default function ProviderListPage() {
  const navigate = useNavigate();
  const [providers, setProviders] = useState<LlmProviderSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchProviders = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getProviders();
      if (result.success && result.data) {
        setProviders(result.data);
      } else {
        setError(result.message ?? "Failed to load providers");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "Failed to load providers");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchProviders();
  }, [fetchProviders]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="Provider 列表"
        actions={
          <Button size="sm" onClick={() => navigate("/providers/new")}>
            <Plus className="mr-2 h-4 w-4" />
            新建 Provider
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
      {/* Content */}
      {loading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      ) : error ? (
        <div className="flex flex-col items-center gap-4 py-20">
          <p className="text-destructive">{error}</p>
          <Button variant="outline" onClick={fetchProviders}>
            <RefreshCw className="mr-2 h-4 w-4" />
            重试
          </Button>
        </div>
      ) : providers.length === 0 ? (
        <div className="flex flex-col items-center gap-4 py-20 text-muted-foreground">
          <Server className="h-12 w-12" />
          <p>暂无 Provider</p>
          <Button onClick={() => navigate("/providers/new")} variant="outline">
            <Plus className="mr-2 h-4 w-4" />
            添加第一个 Provider
          </Button>
        </div>
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>Base URL</TableHead>
                <TableHead className="text-center">模型数</TableHead>
                <TableHead>创建时间</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {providers.map((provider) => (
                <TableRow key={provider.id}>
                  <TableCell>
                    <Link
                      to={`/providers/${provider.id}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {provider.name}
                    </Link>
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm font-mono">
                    {provider.baseUrl}
                  </TableCell>
                  <TableCell className="text-center">
                    {provider.modelCount}
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {formatDate(provider.createdAt)}
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
