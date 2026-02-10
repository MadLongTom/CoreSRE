import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router";
import { Plus, RefreshCw, Loader2, Trash2 } from "lucide-react";
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
import { AgentTypeBadge } from "@/components/agents/AgentTypeBadge";
import { AgentStatusBadge } from "@/components/agents/AgentStatusBadge";
import { DeleteAgentDialog } from "@/components/agents/DeleteAgentDialog";
import { PageHeader } from "@/components/layout/PageHeader";
import { getAgents, type ApiError } from "@/lib/api/agents";
import type { AgentSummary } from "@/types/agent";
import { AGENT_TYPES } from "@/types/agent";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

export default function AgentListPage() {
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState<string>("all");

  // Delete dialog state
  const [deleteTarget, setDeleteTarget] = useState<{
    id: string;
    name: string;
  } | null>(null);

  const fetchAgents = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const filterValue = typeFilter === "all" ? undefined : typeFilter;
      const result = await getAgents(filterValue);
      if (result.success && result.data) {
        setAgents(result.data);
      } else {
        setError(result.message ?? "Failed to load agents");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "Failed to load agents");
    } finally {
      setLoading(false);
    }
  }, [typeFilter]);

  useEffect(() => {
    fetchAgents();
  }, [fetchAgents]);

  const handleDeleted = () => {
    setDeleteTarget(null);
    fetchAgents();
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="Agent 列表"
        actions={
          <Button asChild size="sm">
            <Link to="/agents/new">
              <Plus className="mr-2 h-4 w-4" />
              新建 Agent
            </Link>
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Filter bar */}
        <div className="flex items-center gap-4">
          <Select value={typeFilter} onValueChange={setTypeFilter}>
            <SelectTrigger className="w-48">
              <SelectValue placeholder="筛选类型" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部类型</SelectItem>
              {AGENT_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {t}
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
          <Button variant="outline" onClick={fetchAgents}>
            <RefreshCw className="mr-2 h-4 w-4" />
            重试
          </Button>
        </div>
      ) : agents.length === 0 ? (
        <div className="flex flex-col items-center gap-4 py-20 text-muted-foreground">
          <p>暂无 Agent</p>
          <Button asChild variant="outline">
            <Link to="/agents/new">
              <Plus className="mr-2 h-4 w-4" />
              创建第一个 Agent
            </Link>
          </Button>
        </div>
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>类型</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>创建时间</TableHead>
                <TableHead className="w-20">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {agents.map((agent) => (
                <TableRow key={agent.id}>
                  <TableCell>
                    <Link
                      to={`/agents/${agent.id}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {agent.name}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <AgentTypeBadge type={agent.agentType} />
                  </TableCell>
                  <TableCell>
                    <AgentStatusBadge status={agent.status} />
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {formatDate(agent.createdAt)}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() =>
                        setDeleteTarget({
                          id: agent.id,
                          name: agent.name,
                        })
                      }
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {/* Delete dialog */}
      {deleteTarget && (
        <DeleteAgentDialog
          agentId={deleteTarget.id}
          agentName={deleteTarget.name}
          open={!!deleteTarget}
          onOpenChange={(open) => {
            if (!open) setDeleteTarget(null);
          }}
          onDeleted={handleDeleted}
        />
      )}
      </div>
    </div>
  );
}
