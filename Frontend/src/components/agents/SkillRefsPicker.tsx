import { useCallback, useEffect, useState } from "react";
import { Check, X, Loader2, Sparkles, Package, TriangleAlert } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getSkills } from "@/lib/api/skills";
import type { SkillRegistration } from "@/types/skill";

interface Props {
  value: string[];
  onChange: (ids: string[]) => void;
  /** 当前 Agent 是否启用了沙箱环境 */
  sandboxEnabled?: boolean;
}

/**
 * Skill 多选绑定组件 — 搜索并选择要绑定到 Agent 的 Skill。
 */
export default function SkillRefsPicker({ value, onChange, sandboxEnabled = false }: Props) {
  const [skills, setSkills] = useState<SkillRegistration[]>([]);
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState("");

  // Fetch available skills
  const fetchSkills = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getSkills({ status: "Active", search: search || undefined, pageSize: 50 });
      if (result.success && result.data) {
        setSkills(result.data.items);
      }
    } catch {
      // silently ignore
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    fetchSkills();
  }, [fetchSkills]);

  const toggle = useCallback(
    (id: string) => {
      onChange(
        value.includes(id) ? value.filter((v) => v !== id) : [...value, id],
      );
    },
    [value, onChange],
  );

  const remove = useCallback(
    (id: string) => {
      onChange(value.filter((v) => v !== id));
    },
    [value, onChange],
  );

  const selectedSkills = skills.filter((s) => value.includes(s.id));
  const availableSkills = skills.filter((s) => !value.includes(s.id));

  // 检查是否有文件包 Skill 被选中但沙箱未启用
  const hasFileSkillsWithoutSandbox =
    !sandboxEnabled && selectedSkills.some((s) => s.hasFiles);

  return (
    <div className="space-y-3">
      {/* Warning: file-package skills without sandbox */}
      {hasFileSkillsWithoutSandbox && (
        <div className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 dark:border-amber-700 dark:bg-amber-950/30 p-2.5 text-xs text-amber-800 dark:text-amber-300">
          <TriangleAlert className="h-4 w-4 shrink-0 mt-0.5" />
          <span>
            以下 Skill 包含文件包（📦），需要启用沙箱环境才能使用文件读取和执行功能。未启用沙箱时，这些 Skill 的文件包将不可用，仅 Markdown 内容生效。
          </span>
        </div>
      )}
      {/* Selected tags */}
      {value.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {selectedSkills.map((skill) => (
            <Badge key={skill.id} variant="secondary" className="gap-1 pr-1">
              <Sparkles className="h-3 w-3" />
              {skill.name}
              {skill.hasFiles && (
                <Package className="h-3 w-3 text-muted-foreground" title="包含文件包" />
              )}
              {skill.hasFiles && !sandboxEnabled && (
                <TriangleAlert className="h-3 w-3 text-amber-500" title="需要沙箱" />
              )}
              <Button
                variant="ghost"
                size="icon"
                className="h-4 w-4 p-0 hover:bg-transparent"
                onClick={() => remove(skill.id)}
              >
                <X className="h-3 w-3" />
              </Button>
            </Badge>
          ))}
          {/* Show IDs for skills not yet loaded */}
          {value
            .filter((id) => !selectedSkills.find((s) => s.id === id))
            .map((id) => (
              <Badge key={id} variant="outline" className="gap-1 pr-1 text-xs">
                {id.slice(0, 8)}...
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-4 w-4 p-0 hover:bg-transparent"
                  onClick={() => remove(id)}
                >
                  <X className="h-3 w-3" />
                </Button>
              </Badge>
            ))}
        </div>
      )}

      {/* Search input */}
      <Input
        placeholder="搜索 Skill..."
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="h-8 text-sm"
      />

      {/* Available list */}
      <div className="max-h-48 overflow-y-auto rounded-md border">
        {loading ? (
          <div className="flex items-center justify-center py-4">
            <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
          </div>
        ) : availableSkills.length === 0 ? (
          <p className="py-4 text-center text-sm text-muted-foreground">
            {skills.length === 0 ? "暂无可用 Skill" : "所有 Skill 已绑定"}
          </p>
        ) : (
          availableSkills.map((skill) => (
            <button
              key={skill.id}
              type="button"
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent transition-colors"
              onClick={() => toggle(skill.id)}
            >
              <Sparkles className="h-3.5 w-3.5 text-muted-foreground shrink-0" />
              <span className="font-medium">{skill.name}</span>
              {skill.hasFiles && (
                <Package className="h-3.5 w-3.5 text-muted-foreground shrink-0" title="包含文件包" />
              )}
              <span className="text-xs text-muted-foreground truncate">{skill.description}</span>
              <Badge variant="outline" className="ml-auto text-xs shrink-0">{skill.category}</Badge>
            </button>
          ))
        )}
      </div>
    </div>
  );
}
