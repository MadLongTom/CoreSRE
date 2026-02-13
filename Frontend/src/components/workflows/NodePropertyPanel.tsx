import { useState, useEffect, useCallback } from "react";
import type { Node, Edge } from "@xyflow/react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { WorkflowCodeEditor } from "./WorkflowCodeEditor";
import type { DagNodeData, DagEdgeData, WorkflowEdgeType } from "@/types/workflow";
import { WORKFLOW_EDGE_TYPES } from "@/types/workflow";
import type { AgentSummary } from "@/types/agent";
import type { ToolRegistration } from "@/types/tool";
import { getAgents } from "@/lib/api/agents";
import { getTools } from "@/lib/api/tools";

interface NodePropertyPanelProps {
  selectedNode: Node<DagNodeData> | null;
  selectedEdge: Edge<DagEdgeData> | null;
  onNodeChange: (id: string, data: Partial<DagNodeData>) => void;
  onEdgeChange: (id: string, data: Partial<DagEdgeData>) => void;
  className?: string;
}

/** 节点类型的中文标签 */
const NODE_TYPE_LABELS: Record<string, string> = {
  Start: "开始",
  Agent: "Agent",
  Tool: "Tool",
  Condition: "条件分支",
  FanOut: "并行分发",
  FanIn: "聚合汇总",
};

export function NodePropertyPanel({
  selectedNode,
  selectedEdge,
  onNodeChange,
  onEdgeChange,
  className,
}: NodePropertyPanelProps) {
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [tools, setTools] = useState<ToolRegistration[]>([]);

  // Load agents and tools for reference selection
  useEffect(() => {
    getAgents().then((res) => {
      if (res.success && res.data) setAgents(res.data);
    }).catch(() => {});

    getTools().then((res) => {
      if (res.success && res.data && res.data.items) {
        setTools(res.data.items);
      }
    }).catch(() => {});
  }, []);

  const handleDisplayNameChange = useCallback(
    (value: string) => {
      if (selectedNode) {
        onNodeChange(selectedNode.id, { displayName: value });
      }
    },
    [selectedNode, onNodeChange],
  );

  const handleReferenceIdChange = useCallback(
    (value: string) => {
      if (selectedNode) {
        onNodeChange(selectedNode.id, {
          referenceId: value || null,
        });
      }
    },
    [selectedNode, onNodeChange],
  );

  const handleConfigChange = useCallback(
    (key: string, value: unknown) => {
      if (selectedNode) {
        const newConfig = { ...selectedNode.data.config, [key]: value };
        // Remove keys with empty string values
        if (value === "" || value === null || value === undefined) {
          delete newConfig[key];
        }
        onNodeChange(selectedNode.id, { config: newConfig });
      }
    },
    [selectedNode, onNodeChange],
  );

  const handleInputCountChange = useCallback(
    (value: number) => {
      if (selectedNode) {
        const min = selectedNode.data.nodeType === "Start" ? 0 : 1;
        onNodeChange(selectedNode.id, { inputCount: Math.max(min, value) });
      }
    },
    [selectedNode, onNodeChange],
  );

  const handleOutputCountChange = useCallback(
    (value: number) => {
      if (selectedNode) {
        const min = selectedNode.data.nodeType === "Condition" ? 2 : 1;
        onNodeChange(selectedNode.id, { outputCount: Math.max(min, value) });
      }
    },
    [selectedNode, onNodeChange],
  );

  const handleEdgeTypeChange = useCallback(
    (value: string) => {
      if (selectedEdge) {
        // When switching to Conditional, auto-fill a default condition if empty
        const existingCondition = selectedEdge.data?.condition;
        const defaultCondition = "$input.output.length > 0";
        onEdgeChange(selectedEdge.id, {
          edgeType: value as WorkflowEdgeType,
          condition: value === "Normal" ? null : (existingCondition || defaultCondition),
        });
      }
    },
    [selectedEdge, onEdgeChange],
  );

  const handleConditionChange = useCallback(
    (value: string) => {
      if (selectedEdge) {
        onEdgeChange(selectedEdge.id, { condition: value || null });
      }
    },
    [selectedEdge, onEdgeChange],
  );

  if (!selectedNode && !selectedEdge) {
    return (
      <div className={`text-sm text-muted-foreground ${className ?? ""}`}>
        <h3 className="font-medium mb-2">属性面板</h3>
        <p>选择一个节点或边来编辑属性</p>
      </div>
    );
  }

  if (selectedNode) {
    const data = selectedNode.data;
    const nodeType = data.nodeType;

    return (
      <div className={`space-y-4 ${className ?? ""}`}>
        <h3 className="text-sm font-medium">节点属性</h3>

        {/* ---- 通用字段 ---- */}
        <div className="space-y-2">
          <Label htmlFor="displayName">显示名称</Label>
          <Input
            id="displayName"
            value={data.displayName}
            onChange={(e) => handleDisplayNameChange(e.target.value)}
          />
        </div>
        <div className="space-y-2">
          <Label>节点类型</Label>
          <div className="text-sm text-muted-foreground px-3 py-2 bg-muted rounded-md">
            {NODE_TYPE_LABELS[nodeType] ?? nodeType}
          </div>
        </div>

        {/* ---- Start 特有 ---- */}
        {nodeType === "Start" && (
          <div className="text-xs text-muted-foreground">
            开始节点没有输入端口，工作流执行时的初始数据将从此节点流出。
          </div>
        )}

        {/* ---- Agent 特有 ---- */}
        {nodeType === "Agent" && (
          <>
            <div className="space-y-2">
              <Label htmlFor="referenceId">关联 Agent</Label>
              <Select
                value={data.referenceId ?? ""}
                onValueChange={handleReferenceIdChange}
              >
                <SelectTrigger>
                  <SelectValue placeholder="选择 Agent..." />
                </SelectTrigger>
                <SelectContent>
                  {agents.map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>系统提示词覆盖</Label>
              <WorkflowCodeEditor
                value={(data.config.systemPrompt as string) ?? ""}
                onChange={(v) => handleConfigChange("systemPrompt", v)}
                language="handlebars-like"
                height={100}
                placeholder="留空使用 Agent 默认系统提示词"
              />
            </div>
            <div className="space-y-2">
              <Label>用户提示词模板</Label>
              <WorkflowCodeEditor
                value={(data.config.userPrompt as string) ?? ""}
                onChange={(v) => handleConfigChange("userPrompt", v)}
                language="handlebars-like"
                height={100}
                placeholder="支持 {{ $input.field }} 表达式，留空使用上游输出"
              />
            </div>
          </>
        )}

        {/* ---- Tool 特有 ---- */}
        {nodeType === "Tool" && (
          <div className="space-y-2">
            <Label htmlFor="referenceId">关联 Tool</Label>
            <Select
              value={data.referenceId ?? ""}
              onValueChange={handleReferenceIdChange}
            >
              <SelectTrigger>
                <SelectValue placeholder="选择 Tool..." />
              </SelectTrigger>
              <SelectContent>
                {tools.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        {/* ---- Condition 特有 ---- */}
        {nodeType === "Condition" && (
          <div className="text-xs text-muted-foreground">
            条件表达式配置在<strong>连接边</strong>上。选择边类型为 Conditional
            并填写条件表达式。
          </div>
        )}

        {/* ---- FanOut 特有 ---- */}
        {nodeType === "FanOut" && (
          <div className="text-xs text-muted-foreground">
            并行分发节点将输入数据复制到所有输出端口，下游节点并行执行。
          </div>
        )}

        {/* ---- FanIn 特有 ---- */}
        {nodeType === "FanIn" && (
          <div className="text-xs text-muted-foreground">
            聚合汇总节点等待所有输入端口的数据到达后，合并输出。
          </div>
        )}

        {/* ---- 端口数编辑 ---- */}
        <div className="border-t pt-3 space-y-3">
          <h4 className="text-xs font-medium text-muted-foreground">端口配置</h4>
          {nodeType !== "Start" && (
            <div className="space-y-2">
              <Label htmlFor="inputCount">输入端口数</Label>
              <Input
                id="inputCount"
                type="number"
                min={1}
                value={data.inputCount ?? 1}
                onChange={(e) => handleInputCountChange(parseInt(e.target.value) || 1)}
              />
            </div>
          )}
          <div className="space-y-2">
            <Label htmlFor="outputCount">输出端口数</Label>
            <Input
              id="outputCount"
              type="number"
              min={nodeType === "Condition" ? 2 : 1}
              value={data.outputCount ?? 1}
              onChange={(e) => handleOutputCountChange(parseInt(e.target.value) || 1)}
            />
          </div>
        </div>
      </div>
    );
  }

  if (selectedEdge) {
    const data = selectedEdge.data;
    return (
      <div className={`space-y-4 ${className ?? ""}`}>
        <h3 className="text-sm font-medium">边属性</h3>
        <div className="space-y-2">
          <Label htmlFor="edgeType">边类型</Label>
          <Select
            value={data?.edgeType ?? "Normal"}
            onValueChange={handleEdgeTypeChange}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {WORKFLOW_EDGE_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {t}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {data?.edgeType === "Conditional" && (
          <div className="space-y-2">
            <Label>条件表达式</Label>
            <WorkflowCodeEditor
              value={data.condition ?? ""}
              onChange={handleConditionChange}
              language="javascript"
              height={80}
              placeholder='$input.severity === "high"'
            />
          </div>
        )}
        <div className="space-y-2">
          <Label htmlFor="sourcePortIndex">源端口索引</Label>
          <Input
            id="sourcePortIndex"
            type="number"
            min={0}
            value={data?.sourcePortIndex ?? 0}
            onChange={(e) => {
              if (selectedEdge) {
                onEdgeChange(selectedEdge.id, {
                  sourcePortIndex: parseInt(e.target.value) || 0,
                });
              }
            }}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="targetPortIndex">目标端口索引</Label>
          <Input
            id="targetPortIndex"
            type="number"
            min={0}
            value={data?.targetPortIndex ?? 0}
            onChange={(e) => {
              if (selectedEdge) {
                onEdgeChange(selectedEdge.id, {
                  targetPortIndex: parseInt(e.target.value) || 0,
                });
              }
            }}
          />
        </div>
      </div>
    );
  }

  return null;
}
