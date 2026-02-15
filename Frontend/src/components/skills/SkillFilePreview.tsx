import { useCallback, useEffect, useState } from "react";
import Editor, { loader } from "@monaco-editor/react";
import * as monaco from "monaco-editor";
import editorWorker from "monaco-editor/esm/vs/editor/editor.worker?worker";
import jsonWorker from "monaco-editor/esm/vs/language/json/json.worker?worker";
import tsWorker from "monaco-editor/esm/vs/language/typescript/ts.worker?worker";
import { Loader2, FileWarning, Image, FileText } from "lucide-react";
import { downloadSkillFileText, getSkillFileUrl } from "@/lib/api/skills";

// ---------------------------------------------------------------------------
// Monaco worker config (shared with WorkflowCodeEditor)
// ---------------------------------------------------------------------------

if (!self.MonacoEnvironment) {
  self.MonacoEnvironment = {
    getWorker(_, label) {
      if (label === "json") return new jsonWorker();
      if (label === "typescript" || label === "javascript") return new tsWorker();
      return new editorWorker();
    },
  };
  loader.config({ monaco });
}

// ---------------------------------------------------------------------------
// File type classification
// ---------------------------------------------------------------------------

type PreviewType = "code" | "image" | "pdf" | "office" | "binary";

const IMAGE_EXTS = new Set(["png", "jpg", "jpeg", "gif", "svg", "webp", "ico", "bmp"]);
const CODE_EXTS = new Set([
  "txt", "md", "markdown", "json", "yaml", "yml", "xml", "toml", "ini", "cfg", "conf",
  "js", "mjs", "ts", "tsx", "jsx", "py", "cs", "java", "go", "rs", "c", "cpp", "h", "hpp",
  "sh", "bash", "ps1", "bat", "cmd", "rb", "php", "swift", "kt", "scala", "lua", "r",
  "sql", "csv", "log", "html", "htm", "css", "scss", "less", "dockerfile",
  "makefile", "cmake", "gradle", "sbt", "proto",
]);
const PDF_EXTS = new Set(["pdf"]);
const OFFICE_EXTS = new Set(["doc", "docx", "xls", "xlsx", "ppt", "pptx"]);

function getPreviewType(filename: string): PreviewType {
  const ext = filename.split(".").pop()?.toLowerCase() ?? "";
  // Files without extension are likely text (e.g., Makefile, Dockerfile)
  const baseName = filename.split("/").pop()?.toLowerCase() ?? "";
  if (!ext || baseName === ext) {
    const textNames = new Set(["makefile", "dockerfile", "readme", "license", "changelog", "gitignore"]);
    if (textNames.has(baseName)) return "code";
  }
  if (IMAGE_EXTS.has(ext)) return "image";
  if (PDF_EXTS.has(ext)) return "pdf";
  if (OFFICE_EXTS.has(ext)) return "office";
  if (CODE_EXTS.has(ext)) return "code";
  return "binary";
}

function getMonacoLanguage(filename: string): string {
  const ext = filename.split(".").pop()?.toLowerCase() ?? "";
  const map: Record<string, string> = {
    js: "javascript", mjs: "javascript", ts: "typescript", tsx: "typescript", jsx: "javascript",
    json: "json", yaml: "yaml", yml: "yaml", xml: "xml", html: "html", htm: "html",
    css: "css", scss: "scss", less: "less", md: "markdown", markdown: "markdown",
    py: "python", cs: "csharp", java: "java", go: "go", rs: "rust",
    c: "c", cpp: "cpp", h: "c", hpp: "cpp",
    sh: "shell", bash: "shell", ps1: "powershell", bat: "bat", cmd: "bat",
    rb: "ruby", php: "php", swift: "swift", kt: "kotlin", scala: "scala", lua: "lua", r: "r",
    sql: "sql", dockerfile: "dockerfile", makefile: "makefile",
    toml: "ini", ini: "ini", cfg: "ini", conf: "ini",
    csv: "plaintext", log: "plaintext", txt: "plaintext",
    proto: "protobuf",
  };
  return map[ext] ?? "plaintext";
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface SkillFilePreviewProps {
  skillId: string;
  /** Full file key (with skillId prefix) */
  fileKey: string;
  /** Whether the file is read-only (e.g., builtin skills) */
  readOnly?: boolean;
  /** Called when content changes in the editor */
  onContentChange?: (content: string) => void;
  /**
   * When provided, the editor uses this string instead of fetching from the
   * server. Useful for editing in-memory content such as the Skill markdown
   * body that lives in the parent component's state.
   */
  contentOverride?: string;
  /**
   * Called when content changes while using contentOverride mode.
   * If omitted, the editor falls back to `onContentChange`.
   */
  onContentOverrideChange?: (content: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function SkillFilePreview({
  skillId,
  fileKey,
  readOnly = false,
  onContentChange,
  contentOverride,
  onContentOverrideChange,
}: SkillFilePreviewProps) {
  const [content, setContent] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isOverrideMode = contentOverride !== undefined;

  const filename = fileKey.includes("/")
    ? fileKey.split("/").slice(1).join("/") // strip skillId prefix
    : fileKey;
  const previewType = getPreviewType(filename);

  const fetchContent = useCallback(async () => {
    if (isOverrideMode) return; // skip fetch — using parent-provided content
    if (previewType !== "code") return;
    setLoading(true);
    setError(null);
    try {
      const text = await downloadSkillFileText(skillId, filename);
      setContent(text);
    } catch (err) {
      setError((err as Error).message ?? "加载文件失败");
    } finally {
      setLoading(false);
    }
  }, [skillId, filename, previewType, isOverrideMode]);

  useEffect(() => {
    if (!isOverrideMode) {
      setContent(null);
      setError(null);
    }
    fetchContent();
  }, [fetchContent, isOverrideMode]);

  // ── Code editor ──
  if (previewType === "code") {
    // In override mode, we always have content ready
    if (!isOverrideMode) {
      if (loading) {
        return (
          <div className="flex h-full items-center justify-center">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
        );
      }
      if (error) {
        return (
          <div className="flex h-full flex-col items-center justify-center gap-2 text-sm text-destructive">
            <FileWarning className="h-8 w-8" />
            <span>{error}</span>
          </div>
        );
      }
      if (content === null) return null;
    }

    const editorValue = isOverrideMode ? contentOverride : content!;
    const handleChange = (value: string | undefined) => {
      const v = value ?? "";
      if (isOverrideMode) {
        (onContentOverrideChange ?? onContentChange)?.(v);
      } else {
        onContentChange?.(v);
      }
    };

    return (
      <Editor
        height="100%"
        language={getMonacoLanguage(filename)}
        value={editorValue}
        onChange={handleChange}
        options={{
          readOnly,
          minimap: { enabled: false },
          fontSize: 13,
          lineNumbers: "on",
          scrollBeyondLastLine: false,
          wordWrap: "on",
          automaticLayout: true,
          padding: { top: 8 },
          renderWhitespace: "selection",
          tabSize: 2,
        }}
        theme="vs-dark"
      />
    );
  }

  // ── Image preview ──
  if (previewType === "image") {
    const url = getSkillFileUrl(skillId, filename);
    return (
      <div className="flex h-full items-center justify-center bg-[#1e1e1e] p-4">
        <div className="flex max-h-full max-w-full flex-col items-center gap-3">
          <img
            src={url}
            alt={filename}
            className="max-h-[70vh] max-w-full rounded-md border border-border object-contain"
          />
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <Image className="h-3.5 w-3.5" />
            <span>{filename}</span>
          </div>
        </div>
      </div>
    );
  }

  // ── PDF preview ──
  if (previewType === "pdf") {
    const url = getSkillFileUrl(skillId, filename);
    return (
      <div className="flex h-full flex-col">
        <div className="flex items-center gap-2 border-b px-3 py-1.5 text-xs text-muted-foreground">
          <FileText className="h-3.5 w-3.5 text-red-500" />
          <span>{filename}</span>
        </div>
        <iframe
          src={url}
          className="flex-1 w-full border-0 bg-white"
          title={`PDF: ${filename}`}
        />
      </div>
    );
  }

  // ── Office preview (hint only — browser can't natively render) ──
  if (previewType === "office") {
    const url = getSkillFileUrl(skillId, filename);
    return (
      <div className="flex h-full flex-col items-center justify-center gap-4 p-8 text-center">
        <FileText className="h-12 w-12 text-blue-400" />
        <div className="space-y-2">
          <p className="text-sm font-medium">{filename}</p>
          <p className="text-xs text-muted-foreground">
            Office 文件无法在浏览器中直接预览
          </p>
          <a
            href={url}
            download={filename.split("/").pop()}
            className="inline-block text-xs text-blue-400 hover:text-blue-300 underline"
          >
            下载文件
          </a>
        </div>
      </div>
    );
  }

  // ── Binary / unknown ──
  return (
    <div className="flex h-full flex-col items-center justify-center gap-4 p-8 text-center">
      <FileWarning className="h-12 w-12 text-muted-foreground" />
      <div className="space-y-2">
        <p className="text-sm font-medium">{filename}</p>
        <p className="text-xs text-muted-foreground">
          二进制文件，无法预览
        </p>
        <a
          href={getSkillFileUrl(skillId, filename)}
          download={filename.split("/").pop()}
          className="inline-block text-xs text-blue-400 hover:text-blue-300 underline"
        >
          下载文件
        </a>
      </div>
    </div>
  );
}
