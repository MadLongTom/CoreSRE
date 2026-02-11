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

  const handleEdgeTypeChange = useCallback(
    (value: string) => {
      if (selectedEdge) {
        onEdgeChange(selectedEdge.id, {
          edgeType: value as WorkflowEdgeType,
          condition: value === "Normal" ? null : selectedEdge.data?.condition ?? null,
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
    const showRefSelect =
      data.nodeType === "Agent" || data.nodeType === "Tool";

    return (
      <div className={`space-y-4 ${className ?? ""}`}>
        <h3 className="text-sm font-medium">节点属性</h3>
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
            {data.nodeType}
          </div>
        </div>
        {showRefSelect && (
          <div className="space-y-2">
            <Label htmlFor="referenceId">
              {data.nodeType === "Agent" ? "关联 Agent" : "关联 Tool"}
            </Label>
            <Select
              value={data.referenceId ?? ""}
              onValueChange={handleReferenceIdChange}
            >
              <SelectTrigger>
                <SelectValue placeholder="选择..." />
              </SelectTrigger>
              <SelectContent>
                {data.nodeType === "Agent"
                  ? agents.map((a) => (
                      <SelectItem key={a.id} value={a.id}>
                        {a.name}
                      </SelectItem>
                    ))
                  : tools.map((t) => (
                      <SelectItem key={t.id} value={t.id}>
                        {t.name}
                      </SelectItem>
                    ))}
              </SelectContent>
            </Select>
          </div>
        )}
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
            <Label htmlFor="condition">条件表达式</Label>
            <Input
              id="condition"
              value={data.condition ?? ""}
              onChange={(e) => handleConditionChange(e.target.value)}
              placeholder='$.path == "value"'
            />
          </div>
        )}
      </div>
    );
  }

  return null;
}
