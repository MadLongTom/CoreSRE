import { useCallback, useState } from "react";
import { useNavigate, Link } from "react-router";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import { createSkill } from "@/lib/api/skills";
import type { CreateSkillRequest } from "@/types/skill";
import { SKILL_SCOPES } from "@/types/skill";

export default function SkillCreatePage() {
  const navigate = useNavigate();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [form, setForm] = useState<CreateSkillRequest>({
    name: "",
    description: "",
    category: "",
    content: "",
    scope: "User",
  });

  const updateField = useCallback(
    <K extends keyof CreateSkillRequest>(key: K, value: CreateSkillRequest[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }));
    },
    [],
  );

  const handleSubmit = useCallback(async () => {
    if (!form.name.trim()) {
      setError("名称不能为空");
      return;
    }
    if (!form.description.trim()) {
      setError("描述不能为空");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const result = await createSkill(form);
      if (result.success && result.data) {
        navigate(`/skills/${result.data.id}`);
      } else {
        setError(result.message ?? "创建 Skill 失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "创建 Skill 失败");
    } finally {
      setSaving(false);
    }
  }, [form, navigate]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建 Skill"
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/skills"><ArrowLeft className="h-4 w-4" /></Link>
          </Button>
        }
        actions={
          <Button onClick={handleSubmit} disabled={saving}>
            {saving ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
            创建
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6 max-w-3xl">
        {error && (
          <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
        )}

        <Card>
          <CardHeader>
            <CardTitle>基本信息</CardTitle>
            <CardDescription>定义 Skill 的名称、描述和分类</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="name">名称</Label>
                <Input
                  id="name"
                  value={form.name}
                  onChange={(e) => updateField("name", e.target.value)}
                  placeholder="incident-response"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="scope">范围</Label>
                <Select
                  value={form.scope ?? "User"}
                  onValueChange={(v) => updateField("scope", v)}
                >
                  <SelectTrigger id="scope">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {SKILL_SCOPES.filter((s) => s !== "Builtin").map((s) => (
                      <SelectItem key={s} value={s}>{s}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="category">分类</Label>
              <Input
                id="category"
                value={form.category}
                onChange={(e) => updateField("category", e.target.value)}
                placeholder="SRE"
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="description">描述</Label>
              <Input
                id="description"
                value={form.description}
                onChange={(e) => updateField("description", e.target.value)}
                placeholder="简短描述 Skill 的用途"
              />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Skill 内容 (Markdown)</CardTitle>
            <CardDescription>
              LLM 通过 read_skill 工具按需加载此内容，支持 Markdown 格式
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Textarea
              className="font-mono min-h-[300px]"
              value={form.content}
              onChange={(e) => updateField("content", e.target.value)}
              placeholder={"# Skill Name\n\n## Purpose\nDescribe what this skill does...\n\n## Steps\n1. ...\n2. ..."}
            />
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
