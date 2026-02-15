import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import {
  ArrowLeft,
  Loader2,
  Save,
  Trash2,
  FileUp,
  X,
  PanelLeftClose,
  PanelLeftOpen,
  Download,
  ChevronDown,
  ChevronRight,
  AlertTriangle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";

import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import {
  getSkillById,
  updateSkill,
  deleteSkill,
  getSkillFiles,
  uploadSkillFiles,
  deleteSkillFile,
  exportSkillZip,
} from "@/lib/api/skills";
import type { SkillRegistration, SkillFileEntry } from "@/types/skill";
import {
  SKILL_NAME_PATTERN,
  SKILL_NAME_MAX_LENGTH,
  SKILL_DESCRIPTION_MAX_LENGTH,
  SKILL_BODY_RECOMMENDED_MAX_LINES,
} from "@/types/skill";
import { SkillScopeBadge } from "@/components/skills/SkillScopeBadge";
import { SkillFileTree } from "@/components/skills/SkillFileTree";
import { SkillFilePreview } from "@/components/skills/SkillFilePreview";
import ToolRefsPicker from "@/components/agents/ToolRefsPicker";

export default function SkillDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [skill, setSkill] = useState<SkillRegistration | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Edit form
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState("");
  const [content, setContent] = useState("");

  // Agent Skills spec fields
  const [license, setLicense] = useState("");
  const [compatibility, setCompatibility] = useState("");
  const [allowedTools, setAllowedTools] = useState<string[]>([]);
  const [specExpanded, setSpecExpanded] = useState(false);

  // Files
  const [files, setFiles] = useState<SkillFileEntry[]>([]);
  const [uploading, setUploading] = useState(false);
  const [selectedFile, setSelectedFile] = useState<string | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [activeTab, setActiveTab] = useState<"content" | "file">("content");

  const fileInputRef = useRef<HTMLInputElement>(null);

  // ── Data fetching ──

  const fetchSkill = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    try {
      const result = await getSkillById(id);
      if (result.success && result.data) {
        const s = result.data;
        setSkill(s);
        setName(s.name);
        setDescription(s.description);
        setCategory(s.category);
        setContent(s.content);
        setLicense(s.license ?? "");
        setCompatibility(s.compatibility ?? "");
        setAllowedTools(s.allowedTools ?? []);
      } else {
        setError(result.message ?? "加载 Skill 详情失败");
      }
    } catch (err) {
      setError((err as ApiError).message ?? "加载 Skill 详情失败");
    } finally {
      setLoading(false);
    }
  }, [id]);

  const fetchFiles = useCallback(async () => {
    if (!id) return;
    try {
      const result = await getSkillFiles(id);
      if (result.success && result.data) setFiles(result.data);
    } catch { /* silent */ }
  }, [id]);

  useEffect(() => { fetchSkill(); fetchFiles(); }, [fetchSkill, fetchFiles]);

  // ── Handlers ──

  const handleSave = useCallback(async () => {
    if (!id) return;
    setSaving(true);
    setError(null);
    try {
      const result = await updateSkill(id, {
        name, description, category, content,
        license: license || null,
        compatibility: compatibility || null,
        allowedTools: allowedTools.length > 0 ? allowedTools : undefined,
        requiresTools: skill?.requiresTools,
      });
      if (result.success && result.data) setSkill(result.data);
      else setError(result.message ?? "保存失败");
    } catch (err) {
      setError((err as ApiError).message ?? "保存失败");
    } finally {
      setSaving(false);
    }
  }, [id, name, description, category, content, license, compatibility, allowedTools, skill?.requiresTools]);

  const handleDelete = useCallback(async () => {
    if (!id) return;
    try {
      await deleteSkill(id);
      navigate("/skills");
    } catch (err) {
      setError((err as ApiError).message ?? "删除失败");
    }
  }, [id, navigate]);

  const handleFileUpload = useCallback(async (inputFiles: FileList | null) => {
    if (!id || !inputFiles || inputFiles.length === 0) return;
    setUploading(true);
    try {
      await uploadSkillFiles(id, Array.from(inputFiles));
      await fetchFiles();
      await fetchSkill();
    } catch (err) {
      setError((err as ApiError).message ?? "上传失败");
    } finally {
      setUploading(false);
    }
  }, [id, fetchFiles, fetchSkill]);

  const handleFileDelete = useCallback(async (fileKey: string) => {
    if (!id) return;
    try {
      const relativePath = fileKey.startsWith(`${id}/`) ? fileKey.slice(id.length + 1) : fileKey;
      await deleteSkillFile(id, relativePath);
      if (selectedFile === fileKey) { setSelectedFile(null); setActiveTab("content"); }
      await fetchFiles();
    } catch (err) {
      setError((err as ApiError).message ?? "删除文件失败");
    }
  }, [id, fetchFiles, selectedFile]);

  const handleSelectFile = useCallback((fileKey: string) => {
    setSelectedFile(fileKey);
    setActiveTab("file");
  }, []);

  const handleExportZip = useCallback(async () => {
    if (!id || !skill) return;
    try {
      const blob = await exportSkillZip(id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${skill.name}.zip`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError((err as ApiError).message ?? "导出失败");
    }
  }, [id, skill]);

  // ── Validation ──

  const nameValid = useMemo(() => {
    if (!name) return false;
    if (name.length > SKILL_NAME_MAX_LENGTH) return false;
    if (name.includes("--")) return false;
    return SKILL_NAME_PATTERN.test(name);
  }, [name]);

  const descriptionTooLong = description.length > SKILL_DESCRIPTION_MAX_LENGTH;
  const contentLineCount = content.split("\n").length;
  const bodyTooLong = contentLineCount > SKILL_BODY_RECOMMENDED_MAX_LINES;

  // ── Loading / error states ──

  if (loading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error && !skill) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4">
        <p className="text-destructive">{error}</p>
        <Button variant="outline" onClick={fetchSkill}>重试</Button>
      </div>
    );
  }

  if (!skill || !id) return null;

  const isBuiltin = skill.scope === "Builtin";

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* ── Header ── */}
      <PageHeader
        title={skill.name}
        leading={
          <Button variant="ghost" size="icon" asChild>
            <Link to="/skills"><ArrowLeft className="h-4 w-4" /></Link>
          </Button>
        }
        actions={
          <div className="flex items-center gap-2">
            <SkillScopeBadge scope={skill.scope} />
            <Badge variant={skill.status === "Active" ? "default" : "secondary"}>
              {skill.status === "Active" ? "启用" : "禁用"}
            </Badge>
            {!isBuiltin && (
              <>
                <Button size="sm" onClick={handleSave} disabled={saving}>
                  {saving ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <Save className="mr-1 h-4 w-4" />}
                  保存
                </Button>
                <Button size="sm" variant="destructive" onClick={handleDelete}>
                  <Trash2 className="mr-1 h-4 w-4" /> 删除
                </Button>
              </>
            )}
            <Button size="sm" variant="outline" onClick={handleExportZip} title="导出为 ZIP (Agent Skills 规范)">
              <Download className="mr-1 h-4 w-4" /> 导出
            </Button>
          </div>
        }
      />

      {/* ── Error banner ── */}
      {error && (
        <div className="mx-6 mt-2 rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
      )}

      {/* ── Info bar ── */}
      <div className="border-b px-6 py-3 space-y-2">
        <div className="grid grid-cols-3 gap-4 max-w-4xl">
          <div className="space-y-1">
            <Label htmlFor="name" className="text-xs text-muted-foreground">名称</Label>
            <div className="relative">
              <Input id="name" value={name} onChange={(e) => setName(e.target.value)}
                disabled={isBuiltin} className={`h-8 text-sm font-mono ${name && !nameValid ? "border-destructive" : ""}`}
                placeholder="lowercase-skill-name" maxLength={SKILL_NAME_MAX_LENGTH} />
              {name && !nameValid && (
                <p className="text-[10px] text-destructive mt-0.5">仅限小写字母、数字、连字符，≤64 字符</p>
              )}
            </div>
          </div>
          <div className="space-y-1">
            <Label htmlFor="category" className="text-xs text-muted-foreground">分类</Label>
            <Input id="category" value={category} onChange={(e) => setCategory(e.target.value)}
              disabled={isBuiltin} className="h-8 text-sm" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="description" className="text-xs text-muted-foreground">
              描述 {descriptionTooLong && <span className="text-destructive">(超过 {SKILL_DESCRIPTION_MAX_LENGTH} 字符)</span>}
            </Label>
            <Input id="description" value={description} onChange={(e) => setDescription(e.target.value)}
              disabled={isBuiltin} className={`h-8 text-sm ${descriptionTooLong ? "border-destructive" : ""}`}
              maxLength={SKILL_DESCRIPTION_MAX_LENGTH + 100} />
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
              <Input id="license" value={license} onChange={(e) => setLicense(e.target.value)}
                disabled={isBuiltin} className="h-8 text-sm" placeholder="Apache-2.0" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="compatibility" className="text-xs text-muted-foreground">Compatibility</Label>
              <Input id="compatibility" value={compatibility} onChange={(e) => setCompatibility(e.target.value)}
                disabled={isBuiltin} className="h-8 text-sm" placeholder="Requires docker, git" />
            </div>
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">Allowed Tools</Label>
              {isBuiltin ? (
                <p className="text-sm text-muted-foreground">{allowedTools.length > 0 ? `${allowedTools.length} 个工具` : "无"}</p>
              ) : (
                <ToolRefsPicker value={allowedTools} onChange={setAllowedTools} />
              )}
            </div>
          </div>
        )}
      </div>

      {/* ── Main split layout ── */}
      <div className="flex flex-1 min-h-0 overflow-hidden">
        {/* Left sidebar — file tree */}
        {sidebarOpen && (
          <div className="flex flex-col w-60 shrink-0 border-r bg-muted/30">
            <div className="flex items-center justify-between border-b px-3 py-2">
              <span className="text-xs font-medium text-muted-foreground">文件包</span>
              <div className="flex items-center gap-1">
                {!isBuiltin && (
                  <Button size="icon" variant="ghost" className="h-6 w-6"
                    disabled={uploading} onClick={() => fileInputRef.current?.click()} title="上传文件">
                    {uploading
                      ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      : <FileUp className="h-3.5 w-3.5" />}
                  </Button>
                )}
                <Button size="icon" variant="ghost" className="h-6 w-6"
                  onClick={() => setSidebarOpen(false)} title="关闭侧栏">
                  <PanelLeftClose className="h-3.5 w-3.5" />
                </Button>
              </div>
            </div>

            {/* Content tab */}
            <button
              className={`flex items-center gap-2 px-3 py-1.5 text-sm text-left hover:bg-accent border-b ${
                activeTab === "content" ? "bg-accent font-medium" : ""
              }`}
              onClick={() => { setActiveTab("content"); setSelectedFile(null); }}
            >
              <span className="text-base">📝</span>
              <span className="truncate">Skill 内容</span>
            </button>

            {/* File tree */}
            <div className="flex-1 overflow-y-auto">
              <SkillFileTree files={files} skillId={id}
                selectedFile={selectedFile} onSelect={handleSelectFile} />
            </div>

            {/* Delete selected file */}
            {selectedFile && !isBuiltin && (
              <div className="border-t p-2">
                <Button size="sm" variant="ghost"
                  className="w-full text-destructive hover:text-destructive text-xs h-7"
                  onClick={() => handleFileDelete(selectedFile)}>
                  <X className="mr-1 h-3 w-3" /> 删除选中文件
                </Button>
              </div>
            )}
          </div>
        )}

        {/* Right side — editor / preview */}
        <div className="flex flex-1 flex-col min-w-0">
          {!sidebarOpen && (
            <div className="flex items-center border-b px-2 py-1 gap-1">
              <Button size="icon" variant="ghost" className="h-7 w-7"
                onClick={() => setSidebarOpen(true)} title="打开侧栏">
                <PanelLeftOpen className="h-4 w-4" />
              </Button>
              <span className="text-xs text-muted-foreground ml-1">
                {activeTab === "content"
                  ? "Skill 内容 (Markdown)"
                  : selectedFile?.split("/").slice(1).join("/") ?? ""}
              </span>
            </div>
          )}

          <div className="flex-1 min-h-0">
            {activeTab === "content" ? (
              <>
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
                skillId={id}
                fileKey={`${id}/__content__.md`}
                readOnly={isBuiltin}
                contentOverride={content}
                onContentOverrideChange={isBuiltin ? undefined : setContent}
              />
              </>
            ) : selectedFile ? (
              <SkillFilePreview
                key={selectedFile}
                skillId={id}
                fileKey={selectedFile}
                readOnly={isBuiltin}
              />
            ) : (
              <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                选择左侧文件以预览
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Hidden file input */}
      <input ref={fileInputRef} type="file" multiple className="hidden"
        aria-label="上传 Skill 文件"
        onChange={(e) => { handleFileUpload(e.target.files); e.target.value = ""; }} />
    </div>
  );
}
