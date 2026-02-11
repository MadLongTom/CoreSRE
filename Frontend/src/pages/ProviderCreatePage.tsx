import { useState } from "react";
import { useNavigate } from "react-router";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { PageHeader } from "@/components/layout/PageHeader";
import { createProvider, ApiError } from "@/lib/api/providers";

export default function ProviderCreatePage() {
  const navigate = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);

  const [name, setName] = useState("");
  const [baseUrl, setBaseUrl] = useState("");
  const [apiKey, setApiKey] = useState("");

  const handleSubmit = async () => {
    const validationErrors: string[] = [];
    if (!name.trim()) validationErrors.push("名称不能为空");
    if (name.length > 200) validationErrors.push("名称不能超过 200 个字符");
    if (!baseUrl.trim()) validationErrors.push("Base URL 不能为空");
    if (
      baseUrl.trim() &&
      !baseUrl.trim().startsWith("http://") &&
      !baseUrl.trim().startsWith("https://")
    ) {
      validationErrors.push("Base URL 必须以 http:// 或 https:// 开头");
    }
    if (!apiKey.trim()) validationErrors.push("API Key 不能为空");

    if (validationErrors.length > 0) {
      setErrors(validationErrors);
      return;
    }

    setSubmitting(true);
    setErrors([]);

    try {
      const result = await createProvider({
        name: name.trim(),
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim(),
      });
      if (result.success && result.data) {
        navigate(`/providers/${result.data.id}`);
      } else {
        setErrors(result.errors ?? [result.message ?? "创建失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setErrors(["Provider 名称已被占用"]);
      } else {
        setErrors(apiErr.errors ?? [apiErr.message ?? "创建失败，请重试"]);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建 Provider"
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/providers")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
      <div className="max-w-xl space-y-6">
      {/* Error display */}
      {errors.length > 0 && (
        <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
          <ul className="list-disc pl-5 text-sm text-destructive space-y-1">
            {errors.map((e, i) => (
              <li key={i}>{e}</li>
            ))}
          </ul>
        </div>
      )}

      {/* Form */}
      <Card>
        <CardHeader>
          <CardTitle>Provider 信息</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="provider-name">名称 *</Label>
              <Input
                id="provider-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. OpenAI, Azure OpenAI, Ollama"
                maxLength={200}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="provider-baseUrl">Base URL *</Label>
              <Input
                id="provider-baseUrl"
                value={baseUrl}
                onChange={(e) => setBaseUrl(e.target.value)}
                placeholder="https://api.openai.com/v1"
                type="url"
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="provider-apiKey">API Key *</Label>
            <Input
              id="provider-apiKey"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="sk-..."
              type="password"
            />
          </div>
        </CardContent>
      </Card>

      {/* Submit */}
      <div className="flex justify-end gap-3">
        <Button
          variant="outline"
          onClick={() => navigate("/providers")}
          disabled={submitting}
        >
          取消
        </Button>
        <Button onClick={handleSubmit} disabled={submitting}>
          {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          创建 Provider
        </Button>
      </div>
    </div>
    </div>
    </div>
  );
}
