import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ArrowLeft, Loader2, Save, Trash2, FileUp, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

import { PageHeader } from "@/components/layout/PageHeader";
import { ApiError } from "@/lib/api/agents";
import {
  getSkillById,
  updateSkill,
  deleteSkill,
  getSkillFiles,
  uploadSkillFiles,
  deleteSkillFile,
} from "@/lib/api/skills";
import type { SkillRegistration, SkillFileEntry } from "@/types/skill";

import { SkillScopeBadge } from "@/components/skills/SkillScopeBadge";

export default function SkillDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [skill, setSkill] = useState<SkillRegistration | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Edit form state
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState("");
  const [content, setContent] = useState("");

  // Files state
  const [files, setFiles] = useState<SkillFileEntry[]>([]);
  const [uploading, setUploading] = useState(false);

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
      } else {
        setError(result.message ?? "加载 Skill 详情失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "加载 Skill 详情失败");
    } finally {
      setLoading(false);
    }
  }, [id]);

  const fetchFiles = useCallback(async () => {
    if (!id) return;
    try {
      const result = await getSkillFiles(id);
      if (result.success && result.data) {
        setFiles(result.data);
      }
    } catch {
      // silently ignore file listing errors
    }
  }, [id]);

  useEffect(() => {
    fetchSkill();
    fetchFiles();
  }, [fetchSkill, fetchFiles]);

  const handleSave = useCallback(async () => {
    if (!id) return;
    setSaving(true);
    setError(null);
    try {
      const result = await updateSkill(id, {
        name,
        description,
        category,
        content,
        requiresTools: skill?.requiresTools,
      });
      if (result.success && result.data) {
        setSkill(result.data);
      } else {
        setError(result.message ?? "保存 Skill 失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "保存 Skill 失败");
    } finally {
      setSaving(false);
    }
  }, [id, name, description, category, content, skill?.requiresTools]);

  const handleDelete = useCallback(async () => {
    if (!id) return;
    try {
      await deleteSkill(id);
      navigate("/skills");
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr.message ?? "删除 Skill 失败");
    }
  }, [id, navigate]);

  const handleFileUpload = useCallback(
    async (inputFiles: FileList | null) => {
      if (!id || !inputFiles || inputFiles.length === 0) return;
      setUploading(true);
      try {
        await uploadSkillFiles(id, Array.from(inputFiles));
        await fetchFiles();
        // Refresh skill to update hasFiles
        await fetchSkill();
      } catch (err) {
        const apiErr = err as ApiError;
        setError(apiErr.message ?? "上传文件失败");
      } finally {
        setUploading(false);
      }
    },
    [id, fetchFiles, fetchSkill],
  );

  const handleFileDelete = useCallback(
    async (fileKey: string) => {
      if (!id) return;
      try {
        await deleteSkillFile(id, fileKey);
        await fetchFiles();
      } catch (err) {
        const apiErr = err as ApiError;
        setError(apiErr.message ?? "删除文件失败");
      }
    },
    [id, fetchFiles],
  );

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

  if (!skill) return null;

  const isBuiltin = skill.scope === "Builtin";

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
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
          </div>
        }
      />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
        {error && (
          <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
        )}

        {/* Basic Info */}
        <Card>
          <CardHeader>
            <CardTitle>基本信息</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="name">名称</Label>
                <Input
                  id="name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  disabled={isBuiltin}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="category">分类</Label>
                <Input
                  id="category"
                  value={category}
                  onChange={(e) => setCategory(e.target.value)}
                  disabled={isBuiltin}
                />
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="description">描述</Label>
              <Input
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                disabled={isBuiltin}
              />
            </div>
            <div className="flex gap-4 text-sm text-muted-foreground">
              <span>创建: {new Date(skill.createdAt).toLocaleString("zh-CN")}</span>
              {skill.updatedAt && (
                <span>更新: {new Date(skill.updatedAt).toLocaleString("zh-CN")}</span>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Content / Markdown Editor */}
        <Card>
          <CardHeader>
            <CardTitle>Skill 内容 (Markdown)</CardTitle>
            <CardDescription>
              LLM 通过 read_skill 工具按需加载此内容
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Textarea
              className="font-mono min-h-[400px]"
              value={content}
              onChange={(e) => setContent(e.target.value)}
              disabled={isBuiltin}
              placeholder="# Skill Name&#10;&#10;## Purpose&#10;..."
            />
          </CardContent>
        </Card>

        {/* File Package */}
        <Card>
          <CardHeader>
            <CardTitle>文件包</CardTitle>
            <CardDescription>
              附加脚本、参考文档或资产文件（通过 read_skill_file 工具访问）
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {!isBuiltin && (
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={uploading}
                  onClick={() => document.getElementById("file-input")?.click()}
                >
                  {uploading ? (
                    <Loader2 className="mr-1 h-4 w-4 animate-spin" />
                  ) : (
                    <FileUp className="mr-1 h-4 w-4" />
                  )}
                  上传文件
                </Button>
                <input
                  id="file-input"
                  type="file"
                  multiple
                  className="hidden"
                  aria-label="上传 Skill 文件"
                  onChange={(e) => handleFileUpload(e.target.files)}
                />
              </div>
            )}

            {files.length === 0 ? (
              <p className="text-sm text-muted-foreground">暂无文件</p>
            ) : (
              <div className="space-y-2">
                {files.map((file) => (
                  <div
                    key={file.key}
                    className="flex items-center justify-between rounded-md border px-3 py-2"
                  >
                    <div className="flex flex-col">
                      <span className="text-sm font-mono">{file.key}</span>
                      <span className="text-xs text-muted-foreground">
                        {formatFileSize(file.size)} &middot;{" "}
                        {new Date(file.lastModified).toLocaleString("zh-CN")}
                      </span>
                    </div>
                    {!isBuiltin && (
                      <Button
                        size="icon"
                        variant="ghost"
                        className="text-destructive"
                        onClick={() => handleFileDelete(file.key)}
                      >
                        <X className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
