import { useCallback, useEffect, useState } from "react";
import { Check, X, Loader2, Sparkles } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getSkills } from "@/lib/api/skills";
import type { SkillRegistration } from "@/types/skill";

interface Props {
  value: string[];
  onChange: (ids: string[]) => void;
}

/**
 * Skill 多选绑定组件 — 搜索并选择要绑定到 Agent 的 Skill。
 */
export default function SkillRefsPicker({ value, onChange }: Props) {
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

  return (
    <div className="space-y-3">
      {/* Selected tags */}
      {value.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {selectedSkills.map((skill) => (
            <Badge key={skill.id} variant="secondary" className="gap-1 pr-1">
              <Sparkles className="h-3 w-3" />
              {skill.name}
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
              <span className="text-xs text-muted-foreground truncate">{skill.description}</span>
              <Badge variant="outline" className="ml-auto text-xs shrink-0">{skill.category}</Badge>
            </button>
          ))
        )}
      </div>
    </div>
  );
}
