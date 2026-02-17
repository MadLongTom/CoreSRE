import { useState } from "react";
import { useNavigate } from "react-router";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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
import { Checkbox } from "@/components/ui/checkbox";
import { PageHeader } from "@/components/layout/PageHeader";
import { createDataSource, ApiError } from "@/lib/api/datasources";
import {
  DATA_SOURCE_CATEGORIES,
  CATEGORY_PRODUCTS,
  AUTH_TYPES,
  categoryLabel,
  authLabel,
} from "@/types/datasource";
import type {
  DataSourceCategory,
  DataSourceProduct,
  AuthType,
} from "@/types/datasource";

// Products that need special fields
const K8S_PRODUCTS = new Set(["Kubernetes"]);
const GIT_PRODUCTS = new Set(["GitHub", "GitLab"]);
const DEPLOYMENT_PRODUCTS = new Set(["Kubernetes", "ArgoCD"]);

export default function DataSourceCreatePage() {
  const navigate = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);

  // Basic info
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState<DataSourceCategory>("Metrics");
  const [product, setProduct] = useState<DataSourceProduct>("Prometheus");

  // Connection config
  const [baseUrl, setBaseUrl] = useState("");
  const [authType, setAuthType] = useState<AuthType>("None");
  const [credential, setCredential] = useState("");
  const [authHeaderName, setAuthHeaderName] = useState("");
  const [tlsSkipVerify, setTlsSkipVerify] = useState(false);
  const [timeoutSeconds, setTimeoutSeconds] = useState(30);
  const [namespace, setNamespace] = useState("");
  const [organization, setOrganization] = useState("");
  const [kubeConfig, setKubeConfig] = useState("");

  // Default query config
  const [defaultNamespace, setDefaultNamespace] = useState("");
  const [maxResults, setMaxResults] = useState<number | undefined>(undefined);
  const [defaultStep, setDefaultStep] = useState("");
  const [defaultIndex, setDefaultIndex] = useState("");

  // Available products based on selected category
  const availableProducts = CATEGORY_PRODUCTS[category];

  const handleCategoryChange = (value: string) => {
    const cat = value as DataSourceCategory;
    setCategory(cat);
    const products = CATEGORY_PRODUCTS[cat];
    if (products.length > 0) {
      setProduct(products[0]);
    }
  };

  const handleSubmit = async () => {
    // Client-side validation
    const validationErrors: string[] = [];
    if (!name.trim()) validationErrors.push("名称不能为空");
    if (!baseUrl.trim() && !K8S_PRODUCTS.has(product)) {
      validationErrors.push("端点 URL 不能为空");
    }
    if (authType !== "None" && !credential.trim()) {
      validationErrors.push("已选择认证方式，凭据不能为空");
    }
    if (authType === "ApiKey" && !authHeaderName.trim()) {
      validationErrors.push("API Key 认证需要指定 Header 名称");
    }
    if (GIT_PRODUCTS.has(product) && !organization.trim()) {
      validationErrors.push(`${product} 需要指定 Organization（格式: owner/repo 或 owner）`);
    }

    if (validationErrors.length > 0) {
      setErrors(validationErrors);
      return;
    }

    setSubmitting(true);
    setErrors([]);

    try {
      const result = await createDataSource({
        name: name.trim(),
        description: description.trim() || undefined,
        category,
        product,
        connectionConfig: {
          baseUrl: baseUrl.trim(),
          authType,
          credential: credential.trim() || undefined,
          authHeaderName: authType === "ApiKey" ? authHeaderName.trim() || undefined : undefined,
          tlsSkipVerify,
          timeoutSeconds,
          namespace: namespace.trim() || undefined,
          organization: organization.trim() || undefined,
          kubeConfig: kubeConfig.trim() || undefined,
        },
        defaultQueryConfig: {
          defaultNamespace: defaultNamespace.trim() || undefined,
          maxResults: maxResults || undefined,
          defaultStep: defaultStep.trim() || undefined,
          defaultIndex: defaultIndex.trim() || undefined,
        },
      });

      if (result.success && result.data) {
        navigate(`/datasources/${result.data.id}`);
      } else {
        setErrors(result.errors ?? [result.message ?? "创建失败"]);
      }
    } catch (err) {
      const apiErr = err as ApiError;
      setErrors(apiErr.errors ?? [apiErr.message ?? "创建失败"]);
    } finally {
      setSubmitting(false);
    }
  };

  const showNamespace = DEPLOYMENT_PRODUCTS.has(product);
  const showOrganization = GIT_PRODUCTS.has(product);
  const showKubeConfig = K8S_PRODUCTS.has(product);
  const showStep = category === "Metrics";
  const showIndex = category === "Logs";

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <PageHeader
        title="新建数据源"
        leading={
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate("/datasources")}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        <div className="mx-auto max-w-3xl space-y-6">
          {/* Errors */}
          {errors.length > 0 && (
            <div className="rounded-md border border-destructive bg-destructive/10 p-4">
              <ul className="list-disc pl-4 text-sm text-destructive space-y-1">
                {errors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Basic Info */}
          <Card>
            <CardHeader>
              <CardTitle>基本信息</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="name">名称 *</Label>
                  <Input
                    id="name"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="例: prod-prometheus"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="category">分类 *</Label>
                  <Select value={category} onValueChange={handleCategoryChange}>
                    <SelectTrigger id="category">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {DATA_SOURCE_CATEGORIES.map((c) => (
                        <SelectItem key={c} value={c}>
                          {categoryLabel[c]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="product">产品 *</Label>
                  <Select value={product} onValueChange={(v) => setProduct(v as DataSourceProduct)}>
                    <SelectTrigger id="product">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {availableProducts.map((p) => (
                        <SelectItem key={p} value={p}>
                          {p}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div /> {/* spacer */}
              </div>

              <div className="space-y-2">
                <Label htmlFor="description">描述</Label>
                <Textarea
                  id="description"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="数据源的用途描述..."
                  rows={2}
                />
              </div>
            </CardContent>
          </Card>

          {/* Connection Config */}
          <Card>
            <CardHeader>
              <CardTitle>连接配置</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="baseUrl">
                    端点 URL {!K8S_PRODUCTS.has(product) && "*"}
                  </Label>
                  <Input
                    id="baseUrl"
                    value={baseUrl}
                    onChange={(e) => setBaseUrl(e.target.value)}
                    placeholder={
                      K8S_PRODUCTS.has(product)
                        ? "留空使用默认 kubeconfig"
                        : "https://prometheus.example.com"
                    }
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="timeout">超时时间 (秒)</Label>
                  <Input
                    id="timeout"
                    type="number"
                    value={timeoutSeconds}
                    onChange={(e) => setTimeoutSeconds(parseInt(e.target.value) || 30)}
                    min={1}
                    max={300}
                  />
                </div>
              </div>

              {/* Namespace / Organization / KubeConfig — conditional */}
              {(showNamespace || showOrganization || showKubeConfig) && (
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                  {showNamespace && (
                    <div className="space-y-2">
                      <Label htmlFor="namespace">默认 Namespace</Label>
                      <Input
                        id="namespace"
                        value={namespace}
                        onChange={(e) => setNamespace(e.target.value)}
                        placeholder="default"
                      />
                    </div>
                  )}
                  {showOrganization && (
                    <div className="space-y-2">
                      <Label htmlFor="organization">
                        Organization *
                      </Label>
                      <Input
                        id="organization"
                        value={organization}
                        onChange={(e) => setOrganization(e.target.value)}
                        placeholder={
                          product === "GitHub"
                            ? "owner/repo 或 owner"
                            : "group/project 或 project-id"
                        }
                      />
                    </div>
                  )}
                </div>
              )}

              {showKubeConfig && (
                <div className="space-y-2">
                  <Label htmlFor="kubeConfig">KubeConfig (Base64)</Label>
                  <Textarea
                    id="kubeConfig"
                    value={kubeConfig}
                    onChange={(e) => setKubeConfig(e.target.value)}
                    placeholder="留空使用默认 kubeconfig；或粘贴 Base64 编码的 kubeconfig 内容"
                    rows={3}
                    className="font-mono text-xs"
                  />
                </div>
              )}

              <div className="flex items-center space-x-2">
                <Checkbox
                  id="tlsSkipVerify"
                  checked={tlsSkipVerify}
                  onCheckedChange={(checked) =>
                    setTlsSkipVerify(checked === true)
                  }
                />
                <Label htmlFor="tlsSkipVerify" className="text-sm font-normal">
                  跳过 TLS 证书验证（不推荐在生产环境使用）
                </Label>
              </div>
            </CardContent>
          </Card>

          {/* Auth Config */}
          <Card>
            <CardHeader>
              <CardTitle>认证配置</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="authType">认证方式</Label>
                  <Select value={authType} onValueChange={(v) => setAuthType(v as AuthType)}>
                    <SelectTrigger id="authType">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {AUTH_TYPES.map((a) => (
                        <SelectItem key={a} value={a}>
                          {authLabel[a]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                {authType === "ApiKey" && (
                  <div className="space-y-2">
                    <Label htmlFor="headerName">API Key Header 名称 *</Label>
                    <Input
                      id="headerName"
                      value={authHeaderName}
                      onChange={(e) => setAuthHeaderName(e.target.value)}
                      placeholder="X-API-Key"
                    />
                  </div>
                )}
              </div>

              {authType !== "None" && (
                <div className="space-y-2">
                  <Label htmlFor="credential">
                    {authType === "ApiKey" ? "API Key" : authType === "Bearer" ? "Token" : "凭据"} *
                  </Label>
                  <Input
                    id="credential"
                    type="password"
                    value={credential}
                    onChange={(e) => setCredential(e.target.value)}
                    placeholder="输入凭据..."
                  />
                </div>
              )}
            </CardContent>
          </Card>

          {/* Default Query Config */}
          <Card>
            <CardHeader>
              <CardTitle>默认查询配置（可选）</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="defaultNamespace">默认 Namespace</Label>
                  <Input
                    id="defaultNamespace"
                    value={defaultNamespace}
                    onChange={(e) => setDefaultNamespace(e.target.value)}
                    placeholder="default"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="maxResults">最大结果数</Label>
                  <Input
                    id="maxResults"
                    type="number"
                    value={maxResults ?? ""}
                    onChange={(e) =>
                      setMaxResults(e.target.value ? parseInt(e.target.value) : undefined)
                    }
                    placeholder="100"
                    min={1}
                  />
                </div>
              </div>

              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                {showStep && (
                  <div className="space-y-2">
                    <Label htmlFor="defaultStep">默认步长 (Step)</Label>
                    <Input
                      id="defaultStep"
                      value={defaultStep}
                      onChange={(e) => setDefaultStep(e.target.value)}
                      placeholder="15s"
                    />
                  </div>
                )}
                {showIndex && (
                  <div className="space-y-2">
                    <Label htmlFor="defaultIndex">默认 Index</Label>
                    <Input
                      id="defaultIndex"
                      value={defaultIndex}
                      onChange={(e) => setDefaultIndex(e.target.value)}
                      placeholder="logs-*"
                    />
                  </div>
                )}
              </div>
            </CardContent>
          </Card>

          {/* Submit */}
          <div className="flex justify-end gap-3 pb-6">
            <Button
              variant="outline"
              onClick={() => navigate("/datasources")}
              disabled={submitting}
            >
              取消
            </Button>
            <Button onClick={handleSubmit} disabled={submitting}>
              {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              创建数据源
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
