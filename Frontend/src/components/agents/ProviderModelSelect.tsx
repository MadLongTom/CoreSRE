import { useCallback, useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { getProviders, getProviderModels, ApiError } from "@/lib/api/providers";
import type { LlmProviderSummary } from "@/types/provider";

interface ProviderModelSelectProps {
  providerId: string | null | undefined;
  modelId: string;
  onProviderChange: (providerId: string | null) => void;
  onModelChange: (modelId: string) => void;
}

export default function ProviderModelSelect({
  providerId,
  modelId,
  onProviderChange,
  onModelChange,
}: ProviderModelSelectProps) {
  const [providers, setProviders] = useState<LlmProviderSummary[]>([]);
  const [models, setModels] = useState<string[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(true);
  const [loadingModels, setLoadingModels] = useState(false);
  const [providerError, setProviderError] = useState<string | null>(null);
  const [modelError, setModelError] = useState<string | null>(null);

  // Load providers on mount
  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoadingProviders(true);
      setProviderError(null);
      try {
        const result = await getProviders();
        if (!cancelled && result.success && result.data) {
          setProviders(result.data);
        } else if (!cancelled) {
          setProviderError(result.message ?? "加载 Provider 列表失败");
        }
      } catch (err) {
        if (!cancelled) {
          const apiErr = err as ApiError;
          setProviderError(apiErr.message ?? "加载 Provider 列表失败");
        }
      } finally {
        if (!cancelled) setLoadingProviders(false);
      }
    };
    load();
    return () => {
      cancelled = true;
    };
  }, []);

  // Load models when provider changes
  const loadModels = useCallback(async (pid: string) => {
    setLoadingModels(true);
    setModelError(null);
    setModels([]);
    try {
      const result = await getProviderModels(pid);
      if (result.success && result.data) {
        setModels(result.data.map((m) => m.id));
      } else {
        setModelError(result.message ?? "加载模型列表失败");
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setModelError(apiErr.message ?? "加载模型列表失败");
    } finally {
      setLoadingModels(false);
    }
  }, []);

  useEffect(() => {
    if (providerId) {
      loadModels(providerId);
    } else {
      setModels([]);
    }
  }, [providerId, loadModels]);

  const handleProviderChange = (value: string) => {
    const pid = value === "__none__" ? null : value;
    onProviderChange(pid);
    onModelChange(""); // Clear model when provider changes
  };

  const handleModelChange = (value: string) => {
    onModelChange(value);
  };

  return (
    <div className="space-y-4">
      {/* Provider select */}
      <div className="space-y-2">
        <Label>Provider</Label>
        {loadingProviders ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            加载 Provider 列表…
          </div>
        ) : providerError ? (
          <p className="text-sm text-destructive">{providerError}</p>
        ) : providers.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            暂无可用 Provider，请先创建 Provider。
          </p>
        ) : (
          <Select
            value={providerId ?? "__none__"}
            onValueChange={handleProviderChange}
          >
            <SelectTrigger>
              <SelectValue placeholder="选择 Provider" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__none__">不使用 Provider</SelectItem>
              {providers.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.name} ({p.modelCount} 模型)
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}
      </div>

      {/* Model select */}
      <div className="space-y-2">
        <Label>Model ID</Label>
        {providerId ? (
          loadingModels ? (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
              加载模型列表…
            </div>
          ) : modelError ? (
            <p className="text-sm text-destructive">{modelError}</p>
          ) : models.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              该 Provider 暂无已发现模型，请先执行模型发现。
            </p>
          ) : (
            <Select value={modelId} onValueChange={handleModelChange}>
              <SelectTrigger>
                <SelectValue placeholder="选择模型" />
              </SelectTrigger>
              <SelectContent>
                {models.map((m) => (
                  <SelectItem key={m} value={m}>
                    {m}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )
        ) : (
          <p className="text-sm text-muted-foreground">
            请先选择 Provider 以加载模型列表
          </p>
        )}
      </div>
    </div>
  );
}
