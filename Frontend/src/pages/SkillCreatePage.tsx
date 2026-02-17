import { useCallback, useMemo, useRef, useState } from "react";
import { useNavigate, Link } from "react-router";
import {
  ArrowLeft,
  Loader2,
  FileUp,
  PanelLeftClose,
  PanelLeftOpen,
  ChevronDown,
  ChevronRight,
  AlertTriangle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import { createSkill, uploadSkillFiles } from "@/lib/api/skills";
import type { CreateSkillRequest } from "@/types/skill";
import {
  SKILL_SCOPES,
  SKILL_NAME_PATTERN,
  SKILL_NAME_MAX_LENGTH,
  SKILL_DESCRIPTION_MAX_LENGTH,
  SKILL_BODY_RECOMMENDED_MAX_LINES,
} from "@/types/skill";
import { SkillFilePreview } from "@/components/skills/SkillFilePreview";
import ToolRefsPicker from "@/components/agents/ToolRefsPicker";

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
    license: undefined,
    compatibility: undefined,
    allowedTools: [],
  });

  // Spec fields expansion
  const [specExpanded, setSpecExpanded] = useState(false);

  // Sidebar
  const [sidebarOpen, setSidebarOpen] = useState(true);

  // Pending files (not yet uploaded — we upload after create)
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const updateField = useCallback(
    <K extends keyof CreateSkillRequest>(key: K, value: CreateSkillRequest[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }));
    },
    [],
  );

  // ── Validation ──

  const nameValid = useMemo(() => {
    if (!form.name) return false;
    if (form.name.length > SKILL_NAME_MAX_LENGTH) return false;
    if (form.name.includes("--")) return false;
    return SKILL_NAME_PATTERN.test(form.name);
  }, [form.name]);

  const descriptionTooLong = form.description.length > SKILL_DESCRIPTION_MAX_LENGTH;
  const contentLineCount = form.content.split("\n").length;
  const bodyTooLong = contentLineCount > SKILL_BODY_RECOMMENDED_MAX_LINES;

  const handleAddFiles = useCallback((inputFiles: FileList | null) => {
    if (!inputFiles || inputFiles.length === 0) return;
    setPendingFiles((prev) => {
      const existing = new Set(prev.map((f) => f.name));
      const added = Array.from(inputFiles).filter((f) => !existing.has(f.name));
      return [...prev, ...added];
    });
  }, []);

  const handleRemovePendingFile = useCallback((name: string) => {
    setPendingFiles((prev) => prev.filter((f) => f.name !== name));
  }, []);

  const handleSubmit = useCallback(async () => {
    if (!form.name.trim()) { setError("名称不能为空"); return; }
    if (!nameValid) { setError("名称格式不合规：仅限小写字母、数字、连字符，≤64 字符"); return; }
    if (!form.description.trim()) { setError("描述不能为空"); return; }

    setSaving(true);
    setError(null);
    try {
      const result = await createSkill({
        ...form,
        license: form.license || undefined,
        compatibility: form.compatibility || undefined,
        allowedTools: form.allowedTools && form.allowedTools.length > 0 ? form.allowedTools : undefined,
      });
      if (result.success && result.data) {
        // Upload pending files
        if (pendingFiles.length > 0) {
          setUploading(true);
          try {
            await uploadSkillFiles(result.data.id, pendingFiles);
          } catch {
            // non-fatal — user can upload later
          }
        }
        navigate(`/skills/${result.data.id}`);
      } else {
        setError(result.message ?? "创建 Skill 失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "创建 Skill 失败");
    } finally {
      setSaving(false);
      setUploading(false);
    }
  }, [form, nameValid, pendingFiles, navigate]);

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
          <Button onClick={handleSubmit} disabled={saving || uploading}>
            {(saving || uploading) ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
            {uploading ? "上传文件中…" : "创建"}
          </Button>
        }
      />

      {/* ── Error banner ── */}
      {error && (
        <div className="mx-6 mt-2 rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
      )}

      {/* ── Info bar (matches edit page) ── */}
      <div className="border-b px-6 py-3 space-y-2">
        <div className="grid grid-cols-3 gap-4 max-w-4xl">
          <div className="space-y-1">
            <Label htmlFor="name" className="text-xs text-muted-foreground">名称</Label>
            <div className="relative">
              <Input id="name" value={form.name} onChange={(e) => updateField("name", e.target.value)}
                className={`h-8 text-sm font-mono ${form.name && !nameValid ? "border-destructive" : ""}`}
                placeholder="lowercase-skill-name" maxLength={SKILL_NAME_MAX_LENGTH} />
              {form.name && !nameValid && (
                <p className="text-[10px] text-destructive mt-0.5">仅限小写字母、数字、连字符，≤64 字符</p>
              )}
            </div>
          </div>
          <div className="space-y-1">
            <Label htmlFor="category" className="text-xs text-muted-foreground">分类</Label>
            <Input id="category" value={form.category} onChange={(e) => updateField("category", e.target.value)}
              className="h-8 text-sm" placeholder="SRE" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="scope" className="text-xs text-muted-foreground">范围</Label>
            <Select value={form.scope ?? "User"} onValueChange={(v) => updateField("scope", v)}>
              <SelectTrigger id="scope" className="h-8 text-sm">
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
        <div className="max-w-4xl">
          <div className="space-y-1">
            <Label htmlFor="description" className="text-xs text-muted-foreground">
              描述 {descriptionTooLong && <span className="text-destructive">(超过 {SKILL_DESCRIPTION_MAX_LENGTH} 字符)</span>}
            </Label>
            <Input id="description" value={form.description} onChange={(e) => updateField("description", e.target.value)}
              className={`h-8 text-sm ${descriptionTooLong ? "border-destructive" : ""}`}
              placeholder="简短描述 Skill 的用途" maxLength={SKILL_DESCRIPTION_MAX_LENGTH + 100} />
          </div>
        </div>

        {/* Spec fields collapsible */}
        <button
          className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
          onClick={() => setSpecExpanded(!specExpanded)}
        >
          {specExpanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
          Agent Skills 规范字段
        </button>
        {specExpanded && (
          <div className="grid grid-cols-3 gap-4 max-w-4xl">
            <div className="space-y-1">
              <Label htmlFor="license" className="text-xs text-muted-foreground">License</Label>
              <Input id="license" value={form.license ?? ""} onChange={(e) => updateField("license", e.target.value || undefined)}
                className="h-8 text-sm" placeholder="Apache-2.0" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="compatibility" className="text-xs text-muted-foreground">Compatibility</Label>
              <Input id="compatibility" value={form.compatibility ?? ""} onChange={(e) => updateField("compatibility", e.target.value || undefined)}
                className="h-8 text-sm" placeholder="Requires docker, git" />
            </div>
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">Allowed Tools</Label>
              <ToolRefsPicker value={form.allowedTools ?? []} onChange={(ids) => updateField("allowedTools", ids)} />
            </div>
          </div>
        )}
      </div>

      {/* ── Main split layout ── */}
      <div className="flex flex-1 min-h-0 overflow-hidden">
        {/* Left sidebar — pending files */}
        {sidebarOpen && (
          <div className="flex flex-col w-60 shrink-0 border-r bg-muted/30">
            <div className="flex items-center justify-between border-b px-3 py-2">
              <span className="text-xs font-medium text-muted-foreground">文件包</span>
              <div className="flex items-center gap-1">
                <Button size="icon" variant="ghost" className="h-6 w-6"
                  onClick={() => fileInputRef.current?.click()} title="添加文件">
                  <FileUp className="h-3.5 w-3.5" />
                </Button>
                <Button size="icon" variant="ghost" className="h-6 w-6"
                  onClick={() => setSidebarOpen(false)} title="关闭侧栏">
                  <PanelLeftClose className="h-3.5 w-3.5" />
                </Button>
              </div>
            </div>

            {/* Content tab */}
            <button
              className="flex items-center gap-2 px-3 py-1.5 text-sm text-left hover:bg-accent border-b bg-accent font-medium"
            >
              <span className="text-base">📝</span>
              <span className="truncate">Skill 内容</span>
            </button>

            {/* Pending file list */}
            <div className="flex-1 overflow-y-auto py-1">
              {pendingFiles.length === 0 ? (
                <div className="flex items-center justify-center h-full text-sm text-muted-foreground p-4">
                  暂无文件（创建后可继续添加）
                </div>
              ) : (
                <div className="text-sm space-y-0.5 px-1">
                  {pendingFiles.map((f) => (
                    <div key={f.name} className="group flex items-center gap-1.5 px-2 py-0.5 rounded-sm hover:bg-accent">
                      <span className="truncate flex-1">{f.name}</span>
                      <span className="text-[10px] text-muted-foreground shrink-0">
                        {f.size < 1024 ? `${f.size}B` : `${(f.size / 1024).toFixed(0)}KB`}
                      </span>
                      <button
                        className="shrink-0 opacity-0 group-hover:opacity-100 transition-opacity p-0.5 rounded hover:bg-destructive/20 text-muted-foreground hover:text-destructive"
                        onClick={() => handleRemovePendingFile(f.name)}
                        title="移除"
                      >
                        ×
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}

        {/* Right side — editor */}
        <div className="flex flex-1 flex-col min-w-0">
          {!sidebarOpen && (
            <div className="flex items-center border-b px-2 py-1 gap-1">
              <Button size="icon" variant="ghost" className="h-7 w-7"
                onClick={() => setSidebarOpen(true)} title="打开侧栏">
                <PanelLeftOpen className="h-4 w-4" />
              </Button>
              <span className="text-xs text-muted-foreground ml-1">Skill 内容 (Markdown)</span>
            </div>
          )}

          <div className="flex-1 min-h-0">
            {bodyTooLong && (
              <div className="flex items-center gap-2 border-b bg-yellow-500/10 px-3 py-1 text-xs text-yellow-600 dark:text-yellow-400">
                <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                <span>
                  内容已 {contentLineCount} 行（建议 ≤{SKILL_BODY_RECOMMENDED_MAX_LINES} 行）。
                  考虑将详细参考材料拆分到 <code>references/</code> 目录。
                </span>
              </div>
            )}
            <SkillFilePreview
              key="__skill_content__"
              skillId="__new__"
              fileKey="__new__/__content__.md"
              readOnly={false}
              contentOverride={form.content}
              onContentOverrideChange={(c) => updateField("content", c)}
            />
          </div>
        </div>
      </div>

      {/* Hidden file input */}
      <input ref={fileInputRef} type="file" multiple className="hidden"
        aria-label="添加 Skill 文件"
        onChange={(e) => { handleAddFiles(e.target.files); e.target.value = ""; }} />
    </div>
  );
}
