import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { AlertMatcher, MatchOp } from "@/types/alert-rule";
import { MATCH_OPS, MATCH_OP_LABELS } from "@/types/alert-rule";
import { Plus, Trash2 } from "lucide-react";

interface MatcherEditorProps {
  matchers: AlertMatcher[];
  onChange: (matchers: AlertMatcher[]) => void;
}

export function MatcherEditor({ matchers, onChange }: MatcherEditorProps) {
  const addMatcher = () => {
    onChange([...matchers, { label: "", op: "Eq", value: "" }]);
  };

  const removeMatcher = (index: number) => {
    onChange(matchers.filter((_, i) => i !== index));
  };

  const updateMatcher = (
    index: number,
    field: keyof AlertMatcher,
    value: string
  ) => {
    const updated = matchers.map((m, i) =>
      i === index ? { ...m, [field]: value } : m
    );
    onChange(updated);
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <label className="text-sm font-medium">标签匹配条件</label>
        <Button variant="outline" size="sm" onClick={addMatcher}>
          <Plus className="mr-1 h-3.5 w-3.5" />
          添加
        </Button>
      </div>

      {matchers.length === 0 && (
        <p className="text-xs text-muted-foreground">
          未添加匹配条件（将匹配所有告警）
        </p>
      )}

      {matchers.map((m, idx) => (
        <div key={idx} className="flex items-center gap-2">
          <Input
            placeholder="标签名"
            value={m.label}
            onChange={(e) => updateMatcher(idx, "label", e.target.value)}
            className="w-36"
          />
          <Select
            value={m.op}
            onValueChange={(v) => updateMatcher(idx, "op", v)}
          >
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {MATCH_OPS.map((op) => (
                <SelectItem key={op} value={op}>
                  {MATCH_OP_LABELS[op]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Input
            placeholder="匹配值"
            value={m.value}
            onChange={(e) => updateMatcher(idx, "value", e.target.value)}
            className="flex-1"
          />
          <Button
            variant="ghost"
            size="icon"
            onClick={() => removeMatcher(idx)}
          >
            <Trash2 className="h-4 w-4 text-destructive" />
          </Button>
        </div>
      ))}
    </div>
  );
}
