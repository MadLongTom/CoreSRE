import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import ProviderModelSelect from "@/components/agents/ProviderModelSelect";
import type { LlmConfig } from "@/types/agent";

interface LlmConfigSectionProps {
  config: LlmConfig;
  editing?: boolean;
  onChange?: (config: LlmConfig) => void;
}

export default function LlmConfigSection({
  config,
  editing = false,
  onChange,
}: LlmConfigSectionProps) {
  const update = (partial: Partial<LlmConfig>) =>
    onChange?.({ ...config, ...partial });

  return (
    <Card>
      <CardHeader>
        <CardTitle>LLM 配置</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {editing ? (
          <>
            <ProviderModelSelect
              providerId={config.providerId ?? null}
              modelId={config.modelId}
              onProviderChange={(pid) =>
                update({ providerId: pid ?? undefined })
              }
              onModelChange={(mid) => update({ modelId: mid })}
            />
            <div className="space-y-2">
              <Label htmlFor="edit-instructions">Instructions</Label>
              <Textarea
                id="edit-instructions"
                value={config.instructions ?? ""}
                onChange={(e) => update({ instructions: e.target.value })}
                rows={4}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="edit-toolRefs">Tool Refs</Label>
              <Input
                id="edit-toolRefs"
                value={config.toolRefs.join(", ")}
                onChange={(e) =>
                  update({
                    toolRefs: e.target.value
                      .split(",")
                      .map((s) => s.trim())
                      .filter(Boolean),
                  })
                }
                placeholder="逗号分隔的 GUID"
              />
            </div>
          </>
        ) : (
          <>
            {config.providerName && (
              <div className="space-y-1">
                <Label className="text-muted-foreground text-xs">Provider</Label>
                <p className="text-sm font-medium">{config.providerName}</p>
              </div>
            )}
            <div className="space-y-1">
              <Label className="text-muted-foreground text-xs">Model ID</Label>
              <p className="text-sm font-medium">{config.modelId || "—"}</p>
            </div>
            <div className="space-y-1">
              <Label className="text-muted-foreground text-xs">
                Instructions
              </Label>
              <p className="text-sm whitespace-pre-wrap">
                {config.instructions || "—"}
              </p>
            </div>
            <div className="space-y-1">
              <Label className="text-muted-foreground text-xs">
                Tool Refs
              </Label>
              {config.toolRefs.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {config.toolRefs.map((ref, i) => (
                    <Badge key={i} variant="secondary" className="font-mono text-xs">
                      {ref}
                    </Badge>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">无</p>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
