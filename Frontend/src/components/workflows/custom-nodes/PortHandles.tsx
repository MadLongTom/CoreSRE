import { Handle, Position } from "@xyflow/react";
import { portHandleId } from "@/lib/dag-utils";

interface PortHandlesProps {
  type: "source" | "target";
  count: number;
  color: string;
  position: Position;
}

/**
 * 渲染多端口 Handle — 根据 count 均匀分布在节点的指定边上。
 * 当 count=1 时行为与原来单端口一样。
 */
export function PortHandles({ type, count, color, position }: PortHandlesProps) {
  if (count <= 1) {
    return (
      <Handle
        type={type}
        position={position}
        id={portHandleId(type, 0)}
        className={`!bg-${color}`}
      />
    );
  }

  return (
    <>
      {Array.from({ length: count }, (_, i) => {
        // Distribute handles evenly: offset as percentage from 20% to 80%
        const pct = ((i + 1) / (count + 1)) * 100;
        const style =
          position === Position.Top || position === Position.Bottom
            ? { left: `${pct}%` }
            : { top: `${pct}%` };

        return (
          <Handle
            key={portHandleId(type, i)}
            type={type}
            position={position}
            id={portHandleId(type, i)}
            className={`!bg-${color}`}
            style={style}
          />
        );
      })}
    </>
  );
}
