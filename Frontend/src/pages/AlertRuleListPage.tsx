import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router";
import { PageHeader } from "@/components/layout/PageHeader";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Loader2, Plus, Bell } from "lucide-react";
import type { AlertRuleDto } from "@/types/alert-rule";
import type { ApiResult } from "@/types/agent";

const API = "/api/alert-rules";

export default function AlertRuleListPage() {
  const navigate = useNavigate();
  const [rules, setRules] = useState<AlertRuleDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchRules = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await fetch(API);
      const result: ApiResult<AlertRuleDto[]> = await resp.json();
      if (result.success) {
        setRules(result.data ?? []);
      } else {
        setError(result.error ?? "Failed to load alert rules");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Network error");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchRules();
  }, [fetchRules]);

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title="告警规则"
        leading={<Bell className="h-5 w-5" />}
        actions={
          <Button size="sm" onClick={() => navigate("/alert-rules/new")}>
            <Plus className="mr-1 h-3.5 w-3.5" />
            创建规则
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        {loading ? (
          <div className="flex items-center justify-center py-16">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="rounded-md bg-destructive/10 p-4 text-sm text-destructive">
            {error}
          </div>
        ) : rules.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 text-muted-foreground">
            <Bell className="mb-2 h-10 w-10" />
            <p className="text-sm">暂无告警规则</p>
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>严重级</TableHead>
                <TableHead>路由</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>匹配器</TableHead>
                <TableHead>冷却 (分钟)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rules.map((r) => (
                <TableRow
                  key={r.id}
                  className="cursor-pointer"
                  onClick={() => navigate(`/alert-rules/${r.id}`)}
                >
                  <TableCell className="font-medium">
                    <Link
                      to={`/alert-rules/${r.id}`}
                      className="hover:underline"
                    >
                      {r.name}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline">{r.severity}</Badge>
                  </TableCell>
                  <TableCell className="text-xs">
                    {r.teamAgentId ? "根因分析" : "SOP 执行"}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={r.status === "Active" ? "default" : "secondary"}
                    >
                      {r.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {r.matchers.length} 条件
                  </TableCell>
                  <TableCell>{r.cooldownMinutes}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
    </div>
  );
}
