import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { FileQuestion } from "lucide-react";

export default function NotFoundPage() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-6">
      <FileQuestion className="h-16 w-16 text-muted-foreground" />
      <div className="text-center space-y-2">
        <h1 className="text-4xl font-bold">404</h1>
        <p className="text-muted-foreground text-lg">页面未找到</p>
      </div>
      <Button asChild>
        <Link to="/agents">返回 Agent 列表</Link>
      </Button>
    </div>
  );
}
