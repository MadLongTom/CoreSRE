import { NavLink } from "react-router";
import { List, Search, Bot, Server, MessageSquare, Wrench, GitBranch, Container, Sparkles } from "lucide-react";
import { cn } from "@/lib/utils";
import { Separator } from "@/components/ui/separator";

const navItems = [
  { to: "/chat", label: "对话", icon: MessageSquare },
  { to: "/agents", label: "Agent 列表", icon: List, end: true },
  { to: "/agents/search", label: "Agent 搜索", icon: Search },
  { to: "/providers", label: "Provider 管理", icon: Server },
  { to: "/tools", label: "工具管理", icon: Wrench },
  { to: "/skills", label: "Skill 管理", icon: Sparkles },
  { to: "/workflows", label: "工作流管理", icon: GitBranch },
  { to: "/sandboxes", label: "沙箱管理", icon: Container },
];

export function Sidebar() {
  return (
    <aside className="flex h-screen w-60 flex-col border-r bg-card">
      {/* Logo / Brand */}
      <div className="flex h-14 items-center gap-2 px-4 font-semibold">
        <Bot className="h-5 w-5" />
        <span>CoreSRE</span>
      </div>

      <Separator />

      {/* Navigation */}
      <nav className="flex-1 space-y-1 p-2">
        {navItems.map(({ to, label, icon: Icon, end }) => (
          <NavLink
            key={to}
            to={to}
            end={end}
            className={({ isActive }) =>
              cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                isActive
                  ? "bg-accent text-accent-foreground"
                  : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
              )
            }
          >
            <Icon className="h-4 w-4" />
            {label}
          </NavLink>
        ))}
      </nav>
    </aside>
  );
}
