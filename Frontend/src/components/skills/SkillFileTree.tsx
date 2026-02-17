import { useMemo, useState } from "react";
import {
  ChevronRight,
  ChevronDown,
  File,
  FileText,
  FileCode,
  FileImage,
  FileSpreadsheet,
  FolderOpen,
  Folder,
  X,
} from "lucide-react";
import type { SkillFileEntry } from "@/types/skill";
import { cn } from "@/lib/utils";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface TreeNode {
  name: string;
  path: string; // full relative path (the key minus the skillId prefix)
  children: TreeNode[];
  file?: SkillFileEntry;
}

interface SkillFileTreeProps {
  files: SkillFileEntry[];
  /** Skill ID — file keys are prefixed with `{id}/` */
  skillId: string;
  selectedFile?: string | null;
  onSelect: (fileKey: string) => void;
  /** Called when user clicks to delete a file */
  onDelete?: (fileKey: string) => void;
  /** When true, hide the delete button */
  readOnly?: boolean;
}

// ---------------------------------------------------------------------------
// File classification helpers
// ---------------------------------------------------------------------------

const CODE_EXTS = new Set([
  "js", "mjs", "ts", "tsx", "jsx", "py", "cs", "java", "go", "rs", "c", "cpp", "h", "hpp",
  "sh", "bash", "ps1", "bat", "cmd", "rb", "php", "swift", "kt", "scala", "lua", "r",
  "sql", "makefile", "dockerfile", "cmake", "gradle", "sbt", "proto",
]);

/** Spec-defined top-level directory names */
const KNOWN_DIRS = new Set(["scripts", "references", "reference", "assets", "code"]);

function classifyFile(relativePath: string): "code" | "reference" {
  const ext = relativePath.split(".").pop()?.toLowerCase() ?? "";
  if (CODE_EXTS.has(ext)) return "code";
  return "reference";
}

// ---------------------------------------------------------------------------
// Build tree from flat file list — with virtual folder grouping
// ---------------------------------------------------------------------------

function buildTree(files: SkillFileEntry[], skillId: string): TreeNode[] {
  const root: TreeNode = { name: "", path: "", children: [] };

  for (const file of files) {
    // Strip skillId prefix: "abc123/src/main.py" → "src/main.py"
    const relativePath = file.key.startsWith(`${skillId}/`)
      ? file.key.slice(skillId.length + 1)
      : file.key;

    // Check if file is already inside a known spec directory
    const firstSegment = relativePath.split("/")[0].toLowerCase();
    const isInKnownDir = KNOWN_DIRS.has(firstSegment) && relativePath.includes("/");

    // Root-level files get classified into virtual folders (code/ or reference/)
    const isRootFile = !relativePath.includes("/");
    let effectivePath = relativePath;

    if (isRootFile && !isInKnownDir) {
      const category = classifyFile(relativePath);
      effectivePath = category === "code" ? `code/${relativePath}` : `reference/${relativePath}`;
    }

    const parts = effectivePath.split("/");
    let current = root;

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      const isFile = i === parts.length - 1;
      const fullPath = parts.slice(0, i + 1).join("/");

      let child = current.children.find((c) => c.name === part);
      if (!child) {
        child = {
          name: part,
          path: fullPath,
          children: [],
          file: isFile ? file : undefined,
        };
        current.children.push(child);
      }
      current = child;
    }
  }

  // Sort: folders first, then files, alphabetically
  const sortNodes = (nodes: TreeNode[]) => {
    nodes.sort((a, b) => {
      const aIsDir = a.children.length > 0 && !a.file;
      const bIsDir = b.children.length > 0 && !b.file;
      if (aIsDir !== bIsDir) return aIsDir ? -1 : 1;
      return a.name.localeCompare(b.name);
    });
    for (const node of nodes) {
      if (node.children.length > 0) sortNodes(node.children);
    }
  };

  sortNodes(root.children);
  return root.children;
}

// ---------------------------------------------------------------------------
// Icon helpers
// ---------------------------------------------------------------------------

function getFileIcon(name: string) {
  const ext = name.split(".").pop()?.toLowerCase() ?? "";
  const imageExts = new Set(["png", "jpg", "jpeg", "gif", "svg", "webp", "ico", "bmp"]);
  const codeExts = new Set([
    "js", "ts", "tsx", "jsx", "py", "cs", "java", "go", "rs", "c", "cpp", "h",
    "sh", "bash", "ps1", "rb", "php", "swift", "kt", "scala", "lua",
  ]);
  const dataExts = new Set(["json", "yaml", "yml", "xml", "toml", "ini", "csv", "sql"]);
  const sheetExts = new Set(["xls", "xlsx", "csv"]);

  if (imageExts.has(ext)) return <FileImage className="h-4 w-4 shrink-0 text-green-500" />;
  if (sheetExts.has(ext)) return <FileSpreadsheet className="h-4 w-4 shrink-0 text-emerald-500" />;
  if (codeExts.has(ext)) return <FileCode className="h-4 w-4 shrink-0 text-blue-400" />;
  if (dataExts.has(ext)) return <FileCode className="h-4 w-4 shrink-0 text-yellow-500" />;
  if (ext === "md" || ext === "txt" || ext === "log")
    return <FileText className="h-4 w-4 shrink-0 text-muted-foreground" />;
  if (ext === "pdf") return <FileText className="h-4 w-4 shrink-0 text-red-500" />;
  return <File className="h-4 w-4 shrink-0 text-muted-foreground" />;
}

// ---------------------------------------------------------------------------
// Agent Skills spec directory semantics
// ---------------------------------------------------------------------------

/** Spec-defined directory names and their descriptions */
const SPEC_DIRECTORIES: Record<string, { label: string; color: string }> = {
  scripts: { label: "脚本", color: "text-purple-500" },
  references: { label: "参考", color: "text-teal-500" },
  reference: { label: "参考", color: "text-teal-500" },
  assets: { label: "资产", color: "text-orange-500" },
  code: { label: "代码", color: "text-blue-500" },
};

function getFolderColor(name: string): string {
  return SPEC_DIRECTORIES[name.toLowerCase()]?.color ?? "text-amber-500";
}

function getSpecBadge(name: string): string | null {
  return SPEC_DIRECTORIES[name.toLowerCase()]?.label ?? null;
}

// ---------------------------------------------------------------------------
// TreeNodeItem
// ---------------------------------------------------------------------------

function TreeNodeItem({
  node,
  depth,
  selectedFile,
  onSelect,
  onDelete,
  readOnly,
}: {
  node: TreeNode;
  depth: number;
  selectedFile?: string | null;
  onSelect: (fileKey: string) => void;
  onDelete?: (fileKey: string) => void;
  readOnly?: boolean;
}) {
  const isDirectory = !node.file && node.children.length > 0;
  const [expanded, setExpanded] = useState(true);

  const isSelected = node.file && node.file.key === selectedFile;
  const folderColor = isDirectory ? getFolderColor(node.name) : "";
  const specBadge = isDirectory ? getSpecBadge(node.name) : null;

  return (
    <div className="group/tree-item">
      <div
        className={cn(
          "flex w-full items-center gap-1.5 rounded-sm px-1 py-0.5 text-left text-sm hover:bg-accent",
          isSelected && "bg-accent text-accent-foreground font-medium",
        )}
        style={{ paddingLeft: `${depth * 16 + 4}px` }}
      >
        <button
          className="flex flex-1 items-center gap-1.5 min-w-0"
          onClick={() => {
            if (isDirectory) {
              setExpanded(!expanded);
            } else if (node.file) {
              onSelect(node.file.key);
            }
          }}
        >
          {isDirectory ? (
            expanded ? (
              <ChevronDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
            ) : (
              <ChevronRight className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
            )
          ) : (
            <span className="w-3.5" />
          )}

          {isDirectory ? (
            expanded ? (
              <FolderOpen className={cn("h-4 w-4 shrink-0", folderColor)} />
            ) : (
              <Folder className={cn("h-4 w-4 shrink-0", folderColor)} />
            )
          ) : (
            getFileIcon(node.name)
          )}

          <span className="truncate">{node.name}</span>
          {specBadge && (
            <span className="ml-auto shrink-0 rounded bg-muted px-1 py-0.5 text-[10px] leading-none text-muted-foreground">
              {specBadge}
            </span>
          )}
        </button>

        {/* Delete button — visible on hover for files only */}
        {!isDirectory && !readOnly && node.file && onDelete && (
          <button
            className="shrink-0 opacity-0 group-hover/tree-item:opacity-100 transition-opacity p-0.5 rounded hover:bg-destructive/20 text-muted-foreground hover:text-destructive"
            onClick={(e) => {
              e.stopPropagation();
              onDelete(node.file!.key);
            }}
            title="删除文件"
          >
            <X className="h-3 w-3" />
          </button>
        )}
      </div>

      {isDirectory && expanded && (
        <div>
          {node.children.map((child) => (
            <TreeNodeItem
              key={child.path}
              node={child}
              depth={depth + 1}
              selectedFile={selectedFile}
              onSelect={onSelect}
              onDelete={onDelete}
              readOnly={readOnly}
            />
          ))}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// SkillFileTree (exported)
// ---------------------------------------------------------------------------

export function SkillFileTree({ files, skillId, selectedFile, onSelect, onDelete, readOnly }: SkillFileTreeProps) {
  const tree = useMemo(() => buildTree(files, skillId), [files, skillId]);

  if (tree.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-sm text-muted-foreground p-4">
        暂无文件
      </div>
    );
  }

  return (
    <div className="py-1 overflow-y-auto text-sm">
      {tree.map((node) => (
        <TreeNodeItem
          key={node.path}
          node={node}
          depth={0}
          selectedFile={selectedFile}
          onSelect={onSelect}
          onDelete={onDelete}
          readOnly={readOnly}
        />
      ))}
    </div>
  );
}
