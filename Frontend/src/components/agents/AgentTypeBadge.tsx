import { Badge } from "@/components/ui/badge";

const typeColors: Record<string, string> = {
  A2A: "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-300",
  ChatClient:
    "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
  Workflow:
    "bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-300",
};

interface AgentTypeBadgeProps {
  type: string;
}

export function AgentTypeBadge({ type }: AgentTypeBadgeProps) {
  return (
    <Badge variant="outline" className={typeColors[type] ?? ""}>
      {type}
    </Badge>
  );
}
