import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { Loader2, Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { AgentTypeBadge } from "@/components/agents/AgentTypeBadge";
import SkillHighlight from "@/components/agents/SkillHighlight";
import { PageHeader } from "@/components/layout/PageHeader";
import { searchAgents, ApiError } from "@/lib/api/agents";
import type { AgentSearchResponse } from "@/types/agent";

const DEBOUNCE_MS = 300;

export default function AgentSearchPage() {
  const [query, setQuery] = useState("");
  const [searching, setSearching] = useState(false);
  const [result, setResult] = useState<AgentSearchResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);

    const trimmed = query.trim();
    if (!trimmed) {
      setResult(null);
      setError(null);
      return;
    }

    timerRef.current = setTimeout(async () => {
      setSearching(true);
      setError(null);
      try {
        const res = await searchAgents(trimmed);
        setResult(res);
      } catch (err) {
        const apiErr = err as ApiError;
        setError(apiErr.message ?? "搜索失败");
        setResult(null);
      } finally {
        setSearching(false);
      }
    }, DEBOUNCE_MS);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [query]);

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader title="搜索 Agent 技能" />

      <div className="flex-1 overflow-y-auto p-6 space-y-6">
      {/* Search input */}
      <div className="relative max-w-2xl">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          className="pl-9"
          placeholder="输入关键词搜索 Agent 技能…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          autoFocus
        />
      </div>

      {/* Validation hint */}
      {!query.trim() && !result && (
        <p className="text-sm text-muted-foreground">
          请输入关键词以搜索 Agent 技能
        </p>
      )}

      {/* Loading */}
      {searching && (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-sm">搜索中…</span>
        </div>
      )}

      {/* Error */}
      {error && <p className="text-sm text-destructive">{error}</p>}

      {/* Results */}
      {result && !searching && (
        <div className="space-y-4">
          <div className="flex items-center gap-3 text-sm text-muted-foreground">
            <span>
              共 <strong className="text-foreground">{result.totalCount}</strong>{" "}
              个结果
            </span>
            <Badge variant="outline">{result.searchMode}</Badge>
          </div>

          {result.results.length === 0 ? (
            <div className="py-10 text-center">
              <p className="text-muted-foreground">
                未找到匹配的 Agent 技能
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {result.results.map((item) => (
                <Link
                  key={item.id}
                  to={`/agents/${item.id}`}
                  className="block"
                >
                  <Card className="transition-shadow hover:shadow-md">
                    <CardHeader className="pb-2">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <CardTitle className="text-base">
                            {item.name}
                          </CardTitle>
                          <AgentTypeBadge type={item.agentType} />
                        </div>
                        {item.similarityScore != null && (
                          <Badge variant="secondary" className="text-xs">
                            {Math.round(item.similarityScore * 100)}% 匹配
                          </Badge>
                        )}
                      </div>
                    </CardHeader>
                    <CardContent>
                      {item.matchedSkills.length > 0 && (
                        <div className="space-y-1">
                          <p className="text-xs text-muted-foreground mb-1">
                            匹配的技能:
                          </p>
                          {item.matchedSkills.map((skill, i) => (
                            <div key={i} className="text-sm">
                              <span className="font-medium">
                                <SkillHighlight
                                  text={skill.name}
                                  query={result.query}
                                />
                              </span>
                              {skill.description && (
                                <span className="text-muted-foreground ml-2">
                                  —{" "}
                                  <SkillHighlight
                                    text={skill.description}
                                    query={result.query}
                                  />
                                </span>
                              )}
                            </div>
                          ))}
                        </div>
                      )}
                    </CardContent>
                  </Card>
                </Link>
              ))}
            </div>
          )}
        </div>
      )}
      </div>
    </div>
  );
}
