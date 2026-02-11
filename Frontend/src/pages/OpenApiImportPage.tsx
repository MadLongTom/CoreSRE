import { useState, useRef } from "react";
import { useNavigate } from "react-router";
import { ArrowLeft, Loader2, Upload, FileText, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { PageHeader } from "@/components/layout/PageHeader";
import { importOpenApi, ApiError } from "@/lib/api/tools";
import type { OpenApiImportResult, AuthType } from "@/types/tool";
import { AUTH_TYPES } from "@/types/tool";

const authLabel: Record<AuthType, string> = {
  None: "无认证",
  ApiKey: "API Key",
  Bearer: "Bearer Token",
  OAuth2: "OAuth 2.0",
};

export default function OpenApiImportPage() {
  const navigate = useNavigate();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [result, setResult] = useState<OpenApiImportResult | null>(null);

  const [file, setFile] = useState<File | null>(null);
  const [baseUrl, setBaseUrl] = useState("");
  const [authType, setAuthType] = useState<AuthType>("None");
  const [credential, setCredential] = useState("");
  const [apiKeyHeaderName, setApiKeyHeaderName] = useState("");

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0] ?? null;
    setFile(f);
  };

  const handleSubmit = async () => {
    const validationErrors: string[] = [];
    if (!file) validationErrors.push("请选择 OpenAPI 规范文件");
    if (authType === "ApiKey" && !credential.trim())
      validationErrors.push("API Key 不能为空");
    if (authType === "Bearer" && !credential.trim())
      validationErrors.push("Bearer Token 不能为空");

    if (validationErrors.length > 0) {
      setErrors(validationErrors);
      return;
    }

    setSubmitting(true);
    setErrors([]);
    setResult(null);

    try {
      const res = await importOpenApi(
        file!,
        baseUrl.trim() || undefined,
        authType !== "None"
          ? {
              authType,
              credential: credential.trim() || undefined,
              apiKeyHeaderName: apiKeyHeaderName.trim() || undefined,
            }
          : undefined,
      );
      if (res.success && res.data) {
        setResult(res.data);
      } else {
        setErrors(res.errors ?? [res.message ?? "导入失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setErrors(apiErr.errors ?? [apiErr.message ?? "导入失败，请重试"]);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="导入 OpenAPI"
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/tools")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        <div className="space-y-6">
          {errors.length > 0 && (
            <div className="rounded-md border border-destructive/50 bg-destructive/10 p-4">
              <ul className="list-disc pl-5 text-sm text-destructive space-y-1">
                {errors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Import result */}
          {result && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <CheckCircle2 className="h-5 w-5 text-green-600" />
                  导入完成
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="flex gap-4 text-sm">
                  <span>
                    总操作数：
                    <Badge variant="outline">{result.totalOperations}</Badge>
                  </span>
                  <span>
                    已导入：
                    <Badge variant="default">{result.importedCount}</Badge>
                  </span>
                  {result.skippedCount > 0 && (
                    <span>
                      已跳过：
                      <Badge variant="secondary">{result.skippedCount}</Badge>
                    </span>
                  )}
                </div>
                {result.errors.length > 0 && (
                  <div className="rounded-md border border-destructive/50 bg-destructive/10 p-3">
                    <ul className="list-disc pl-5 text-xs text-destructive space-y-1">
                      {result.errors.map((e, i) => (
                        <li key={i}>{e}</li>
                      ))}
                    </ul>
                  </div>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => navigate("/tools")}
                >
                  返回工具列表
                </Button>
              </CardContent>
            </Card>
          )}

          {/* Import form */}
          {!result && (
            <>
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <Card>
                <CardHeader>
                  <CardTitle>OpenAPI 规范文件</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="space-y-2">
                    <Label>选择文件 *</Label>
                    <div className="flex items-center gap-3">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => fileInputRef.current?.click()}
                      >
                        <Upload className="mr-2 h-4 w-4" />
                        选择文件
                      </Button>
                      {file && (
                        <span className="flex items-center gap-1 text-sm text-muted-foreground">
                          <FileText className="h-4 w-4" />
                          {file.name}
                        </span>
                      )}
                      <input
                        ref={fileInputRef}
                        type="file"
                        accept=".json,.yaml,.yml"
                        onChange={handleFileChange}
                        className="hidden"
                      />
                    </div>
                    <p className="text-xs text-muted-foreground">
                      支持 JSON 或 YAML 格式的 OpenAPI 3.x 规范文件
                    </p>
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="import-baseUrl">
                      Base URL（可选，覆盖规范中的 servers）
                    </Label>
                    <Input
                      id="import-baseUrl"
                      value={baseUrl}
                      onChange={(e) => setBaseUrl(e.target.value)}
                      placeholder="https://api.example.com"
                      type="url"
                    />
                  </div>
                </CardContent>
              </Card>

              {/* Auth for imported tools */}
              <Card>
                <CardHeader>
                  <CardTitle>认证配置（可选）</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="space-y-2">
                    <Label>认证方式</Label>
                    <Select
                      value={authType}
                      onValueChange={(v) => setAuthType(v as AuthType)}
                    >
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {AUTH_TYPES.filter((a) => a !== "OAuth2").map((a) => (
                          <SelectItem key={a} value={a}>
                            {authLabel[a]}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>

                  {authType === "ApiKey" && (
                    <>
                      <div className="space-y-2">
                        <Label htmlFor="import-apikey">API Key *</Label>
                        <Input
                          id="import-apikey"
                          value={credential}
                          onChange={(e) => setCredential(e.target.value)}
                          type="password"
                        />
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="import-header">Header 名称</Label>
                        <Input
                          id="import-header"
                          value={apiKeyHeaderName}
                          onChange={(e) => setApiKeyHeaderName(e.target.value)}
                          placeholder="X-API-Key"
                        />
                      </div>
                    </>
                  )}

                  {authType === "Bearer" && (
                    <div className="space-y-2">
                      <Label htmlFor="import-bearer">Bearer Token *</Label>
                      <Input
                        id="import-bearer"
                        value={credential}
                        onChange={(e) => setCredential(e.target.value)}
                        type="password"
                      />
                    </div>
                  )}
                </CardContent>
              </Card>
              </div>

              <div className="flex justify-end gap-3">
                <Button
                  variant="outline"
                  onClick={() => navigate("/tools")}
                  disabled={submitting}
                >
                  取消
                </Button>
                <Button onClick={handleSubmit} disabled={submitting}>
                  {submitting && (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  )}
                  导入
                </Button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
