import { useCallback, useRef, useMemo, useState } from "react";
import {
  ReactFlow,
  Background,
  Controls,
  addEdge,
  useNodesState,
  useEdgesState,
  type Connection,
  type Node,
  type Edge,
  type OnConnect,
  type NodeTypes,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";

import { AgentNode } from "@/components/workflows/custom-nodes/AgentNode";
import { ToolNode } from "@/components/workflows/custom-nodes/ToolNode";
import { ConditionNode } from "@/components/workflows/custom-nodes/ConditionNode";
import { FanOutNode } from "@/components/workflows/custom-nodes/FanOutNode";
import { FanInNode } from "@/components/workflows/custom-nodes/FanInNode";
import { NodePanel } from "@/components/workflows/NodePanel";
import { NodePropertyPanel } from "@/components/workflows/NodePropertyPanel";
import { toReactFlowNodes, toReactFlowEdges, fromReactFlowState } from "@/lib/dag-utils";
import type {
  DagNodeData,
  DagEdgeData,
  WorkflowGraph,
  WorkflowNodeType,
} from "@/types/workflow";

const nodeTypes: NodeTypes = {
  Agent: AgentNode,
  Tool: ToolNode,
  Condition: ConditionNode,
  FanOut: FanOutNode,
  FanIn: FanInNode,
};

interface DagEditorProps {
  /** Initial graph data from backend (for edit mode) */
  initialGraph?: WorkflowGraph;
  /** Called whenever graph state changes */
  onChange?: (graph: WorkflowGraph) => void;
  className?: string;
}

let nodeIdCounter = 0;
function generateNodeId(): string {
  nodeIdCounter += 1;
  return `node-${Date.now()}-${nodeIdCounter}`;
}

let edgeIdCounter = 0;
function generateEdgeId(): string {
  edgeIdCounter += 1;
  return `edge-${Date.now()}-${edgeIdCounter}`;
}

export function DagEditor({ initialGraph, onChange, className }: DagEditorProps) {
  const reactFlowWrapper = useRef<HTMLDivElement>(null);

  const initialNodes = useMemo(
    () => (initialGraph ? toReactFlowNodes(initialGraph.nodes) : []),
    [initialGraph],
  );
  const initialEdges = useMemo(
    () => (initialGraph ? toReactFlowEdges(initialGraph.edges) : []),
    [initialGraph],
  );

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);

  const [selectedNode, setSelectedNode] = useState<Node<DagNodeData> | null>(null);
  const [selectedEdge, setSelectedEdge] = useState<Edge<DagEdgeData> | null>(null);

  // Emit graph changes
  const emitChange = useCallback(
    (n: Node<DagNodeData>[], e: Edge<DagEdgeData>[]) => {
      onChange?.(fromReactFlowState(n, e));
    },
    [onChange],
  );

  // Handle new edge connection
  const onConnect: OnConnect = useCallback(
    (connection: Connection) => {
      // Reject self-loops
      if (connection.source === connection.target) {
        return;
      }

      const newEdge: Edge<DagEdgeData> = {
        id: generateEdgeId(),
        source: connection.source,
        target: connection.target,
        sourceHandle: connection.sourceHandle ?? undefined,
        targetHandle: connection.targetHandle ?? undefined,
        data: { edgeType: "Normal", condition: null },
      };

      setEdges((eds) => {
        const updated = addEdge(newEdge, eds);
        // Defer emitChange to avoid state update during render
        setTimeout(() => emitChange(nodes, updated), 0);
        return updated;
      });
    },
    [setEdges, nodes, emitChange],
  );

  // Handle nodes change with graph emission
  const handleNodesChange: typeof onNodesChange = useCallback(
    (changes) => {
      onNodesChange(changes);
      // Defer emitChange
      setTimeout(() => {
        setNodes((currentNodes) => {
          emitChange(currentNodes, edges);
          return currentNodes;
        });
      }, 0);
    },
    [onNodesChange, setNodes, edges, emitChange],
  );

  // Handle edges change with graph emission
  const handleEdgesChange: typeof onEdgesChange = useCallback(
    (changes) => {
      onEdgesChange(changes);
      setTimeout(() => {
        setEdges((currentEdges) => {
          emitChange(nodes, currentEdges);
          return currentEdges;
        });
      }, 0);
    },
    [onEdgesChange, setEdges, nodes, emitChange],
  );

  // Handle drag-and-drop from NodePanel
  const onDragOver = useCallback((event: React.DragEvent) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
  }, []);

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault();

      const nodeType = event.dataTransfer.getData(
        "application/reactflow-nodetype",
      ) as WorkflowNodeType;
      if (!nodeType) return;

      const bounds = reactFlowWrapper.current?.getBoundingClientRect();
      if (!bounds) return;

      const position = {
        x: event.clientX - bounds.left - 90,
        y: event.clientY - bounds.top - 40,
      };

      const newNode: Node<DagNodeData> = {
        id: generateNodeId(),
        type: nodeType,
        position,
        data: {
          nodeType,
          referenceId: null,
          displayName: `New ${nodeType}`,
          config: {},
        },
      };

      setNodes((nds) => {
        const updated = [...nds, newNode];
        setTimeout(() => emitChange(updated, edges), 0);
        return updated;
      });
    },
    [setNodes, edges, emitChange],
  );

  // Node/Edge selection
  const onNodeClick = useCallback(
    (_: React.MouseEvent, node: Node) => {
      setSelectedNode(node as Node<DagNodeData>);
      setSelectedEdge(null);
    },
    [],
  );

  const onEdgeClick = useCallback(
    (_: React.MouseEvent, edge: Edge) => {
      setSelectedEdge(edge as Edge<DagEdgeData>);
      setSelectedNode(null);
    },
    [],
  );

  const onPaneClick = useCallback(() => {
    setSelectedNode(null);
    setSelectedEdge(null);
  }, []);

  // Property panel callbacks
  const handleNodeDataChange = useCallback(
    (id: string, dataUpdate: Partial<DagNodeData>) => {
      setNodes((nds) => {
        const updated = nds.map((n) => {
          if (n.id !== id) return n;
          const newData = { ...n.data, ...dataUpdate };
          return { ...n, data: newData };
        });
        setSelectedNode((prev) =>
          prev?.id === id ? { ...prev, data: { ...prev.data, ...dataUpdate } } : prev,
        );
        setTimeout(() => emitChange(updated, edges), 0);
        return updated;
      });
    },
    [setNodes, edges, emitChange],
  );

  const handleEdgeDataChange = useCallback(
    (id: string, dataUpdate: Partial<DagEdgeData>) => {
      setEdges((eds) => {
        const updated = eds.map((e) => {
          if (e.id !== id) return e;
          const newData = { ...e.data, ...dataUpdate } as DagEdgeData;
          return {
            ...e,
            data: newData,
            animated: newData.edgeType === "Conditional",
            label: newData.edgeType === "Conditional" && newData.condition
              ? newData.condition
              : undefined,
          };
        });
        setSelectedEdge((prev) =>
          prev?.id === id
            ? { ...prev, data: { ...prev.data, ...dataUpdate } as DagEdgeData }
            : prev,
        );
        setTimeout(() => emitChange(nodes, updated), 0);
        return updated;
      });
    },
    [setEdges, nodes, emitChange],
  );

  return (
    <div className={`flex gap-4 ${className ?? ""}`}>
      {/* Left sidebar: node palette */}
      <div className="w-40 shrink-0 border rounded-md p-3">
        <NodePanel />
      </div>

      {/* Center: React Flow canvas */}
      <div
        ref={reactFlowWrapper}
        className="flex-1 h-[500px] border rounded-md"
      >
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={handleNodesChange}
          onEdgesChange={handleEdgesChange}
          onConnect={onConnect}
          onDragOver={onDragOver}
          onDrop={onDrop}
          onNodeClick={onNodeClick}
          onEdgeClick={onEdgeClick}
          onPaneClick={onPaneClick}
          nodeTypes={nodeTypes}
          fitView
          deleteKeyCode={["Backspace", "Delete"]}
        >
          <Background />
          <Controls />
        </ReactFlow>
      </div>

      {/* Right sidebar: properties */}
      <div className="w-56 shrink-0 border rounded-md p-3">
        <NodePropertyPanel
          selectedNode={selectedNode}
          selectedEdge={selectedEdge}
          onNodeChange={handleNodeDataChange}
          onEdgeChange={handleEdgeDataChange}
        />
      </div>
    </div>
  );
}
