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
}

// ---------------------------------------------------------------------------
// Build tree from flat file list
// ---------------------------------------------------------------------------

function buildTree(files: SkillFileEntry[], skillId: string): TreeNode[] {
  const root: TreeNode = { name: "", path: "", children: [] };

  for (const file of files) {
    // Strip skillId prefix: "abc123/src/main.py" → "src/main.py"
    const relativePath = file.key.startsWith(`${skillId}/`)
      ? file.key.slice(skillId.length + 1)
      : file.key;

    const parts = relativePath.split("/");
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
}: {
  node: TreeNode;
  depth: number;
  selectedFile?: string | null;
  onSelect: (fileKey: string) => void;
}) {
  const isDirectory = !node.file && node.children.length > 0;
  const [expanded, setExpanded] = useState(true);

  const isSelected = node.file && node.file.key === selectedFile;
  const folderColor = isDirectory ? getFolderColor(node.name) : "";
  const specBadge = isDirectory ? getSpecBadge(node.name) : null;

  return (
    <div>
      <button
        className={cn(
          "flex w-full items-center gap-1.5 rounded-sm px-1 py-0.5 text-left text-sm hover:bg-accent",
          isSelected && "bg-accent text-accent-foreground font-medium",
        )}
        style={{ paddingLeft: `${depth * 16 + 4}px` }}
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

      {isDirectory && expanded && (
        <div>
          {node.children.map((child) => (
            <TreeNodeItem
              key={child.path}
              node={child}
              depth={depth + 1}
              selectedFile={selectedFile}
              onSelect={onSelect}
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

export function SkillFileTree({ files, skillId, selectedFile, onSelect }: SkillFileTreeProps) {
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
        />
      ))}
    </div>
  );
}
