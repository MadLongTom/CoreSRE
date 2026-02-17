import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { Loader2, Plus, Sparkles, Download, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import { getSkills, deleteSkill, exportSkillZip, importSkillZip } from "@/lib/api/skills";
import type { SkillRegistration } from "@/types/skill";
import { SKILL_SCOPES, SKILL_STATUSES } from "@/types/skill";
import { DeleteSkillDialog } from "@/components/skills/DeleteSkillDialog";
import { SkillScopeBadge } from "@/components/skills/SkillScopeBadge";

export default function SkillListPage() {
  const [skills, setSkills] = useState<SkillRegistration[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [scopeFilter, setScopeFilter] = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [deleteTarget, setDeleteTarget] = useState<SkillRegistration | null>(null);
  const [importing, setImporting] = useState(false);
  const [exportingId, setExportingId] = useState<string | null>(null);
  const importInputRef = useRef<HTMLInputElement>(null);

  const fetchSkills = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getSkills({
        scope: scopeFilter !== "all" ? scopeFilter : undefined,
        status: statusFilter !== "all" ? statusFilter : undefined,
        search: search || undefined,
      });
      if (result.success && result.data) {
        setSkills(result.data.items);
        setTotalCount(result.data.totalCount);
      } else {
        setError(result.message ?? "加载 Skill 列表失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "加载 Skill 列表失败");
    } finally {
      setLoading(false);
    }
  }, [scopeFilter, statusFilter, search]);

  useEffect(() => {
    fetchSkills();
  }, [fetchSkills]);

  const handleDelete = useCallback(async () => {
    if (!deleteTarget) return;
    try {
      await deleteSkill(deleteTarget.id);
      setDeleteTarget(null);
      await fetchSkills();
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "删除 Skill 失败");
    }
  }, [deleteTarget, fetchSkills]);

  const handleImport = useCallback(async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setImporting(true);
    setError(null);
    try {
      await importSkillZip(file);
      await fetchSkills();
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "导入 Skill 失败");
    } finally {
      setImporting(false);
      if (importInputRef.current) importInputRef.current.value = "";
    }
  }, [fetchSkills]);

  const handleExport = useCallback(async (skill: SkillRegistration) => {
    setExportingId(skill.id);
    try {
      const blob = await exportSkillZip(skill.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${skill.name}.zip`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "导出 Skill 失败");
    } finally {
      setExportingId(null);
    }
  }, []);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="Skill 管理"
        actions={
          <div className="flex items-center gap-2">
            <input
              ref={importInputRef}
              type="file"
              accept=".zip"
              className="hidden"
              onChange={handleImport}
            />
            <Button
              size="sm"
              variant="outline"
              disabled={importing}
              onClick={() => importInputRef.current?.click()}
            >
              {importing
                ? <Loader2 className="mr-1 h-4 w-4 animate-spin" />
                : <Upload className="mr-1 h-4 w-4" />}
              导入 Skill
            </Button>
            <Button size="sm" asChild>
              <Link to="/skills/new"><Plus className="mr-1 h-4 w-4" /> 新建 Skill</Link>
            </Button>
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {/* Filter Bar */}
        <div className="flex items-center gap-4 flex-wrap">
          <Input
            placeholder="搜索 Skill..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="max-w-sm"
          />
          <Select value={scopeFilter} onValueChange={setScopeFilter}>
            <SelectTrigger className="w-36">
              <SelectValue placeholder="所有范围" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">所有范围</SelectItem>
              {SKILL_SCOPES.map((s) => (
                <SelectItem key={s} value={s}>{s}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={statusFilter} onValueChange={setStatusFilter}>
            <SelectTrigger className="w-36">
              <SelectValue placeholder="所有状态" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">所有状态</SelectItem>
              {SKILL_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>{s}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="ml-auto text-sm text-muted-foreground">
            共 {totalCount} 个 Skill
          </span>
        </div>

        {/* Content */}
        {loading ? (
          <div className="flex items-center justify-center py-16">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center py-16 gap-4">
            <p className="text-destructive">{error}</p>
            <Button variant="outline" onClick={fetchSkills}>重试</Button>
          </div>
        ) : skills.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 gap-4">
            <Sparkles className="h-12 w-12 text-muted-foreground" />
            <p className="text-muted-foreground">暂无 Skill</p>
            <Button asChild>
              <Link to="/skills/new">创建第一个 Skill</Link>
            </Button>
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>描述</TableHead>
                <TableHead>分类</TableHead>
                <TableHead>范围</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>文件</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {skills.map((skill) => (
                <TableRow key={skill.id}>
                  <TableCell>
                    <Link
                      to={`/skills/${skill.id}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {skill.name}
                    </Link>
                  </TableCell>
                  <TableCell className="max-w-64 truncate text-sm text-muted-foreground">
                    {skill.description}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline">{skill.category}</Badge>
                  </TableCell>
                  <TableCell>
                    <SkillScopeBadge scope={skill.scope} />
                  </TableCell>
                  <TableCell>
                    <Badge variant={skill.status === "Active" ? "default" : "secondary"}>
                      {skill.status === "Active" ? "启用" : "禁用"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-sm">
                    {skill.hasFiles ? "有" : "—"}
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex items-center justify-end gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        title="导出 ZIP"
                        disabled={exportingId === skill.id}
                        onClick={() => handleExport(skill)}
                      >
                        {exportingId === skill.id
                          ? <Loader2 className="h-4 w-4 animate-spin" />
                          : <Download className="h-4 w-4" />}
                      </Button>
                      <Button size="sm" variant="outline" asChild>
                        <Link to={`/skills/${skill.id}`}>编辑</Link>
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-destructive"
                        onClick={() => setDeleteTarget(skill)}
                        disabled={skill.scope === "Builtin"}
                      >
                        删除
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>

      <DeleteSkillDialog
        skill={deleteTarget}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
