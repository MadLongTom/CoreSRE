import { NavLink } from "react-router";
import {
  List,
  Search,
  Bot,
  Server,
  MessageSquare,
  Wrench,
  GitBranch,
  Container,
  Sparkles,
  Database,
  ShieldAlert,
  Bell,
  BarChart3,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";

interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  end?: boolean;
}

interface NavSection {
  /** Section title shown above the items (uppercase, muted). */
  title: string;
  items: NavItem[];
}

/**
 * Navigation grouped to mirror the platform narrative used in the thesis
 * (Chapter 4) and the demo flow (DEMO-PLAN.md scenarios 1–6):
 * 工作台 → 智能体管理 → 能力接入 → 编排与自动化 → AIOps 运维闭环.
 */
const navSections: NavSection[] = [
  {
    title: "工作台",
    items: [{ to: "/chat", label: "对话", icon: MessageSquare }],
  },
  {
    title: "智能体管理",
    items: [
      { to: "/agents", label: "Agent 列表", icon: List, end: true },
      { to: "/agents/search", label: "Agent 搜索", icon: Search },
    ],
  },
  {
    title: "能力接入",
    items: [
      { to: "/providers", label: "Provider 管理", icon: Server },
      { to: "/tools", label: "工具管理", icon: Wrench },
      { to: "/datasources", label: "数据源管理", icon: Database },
      { to: "/sandboxes", label: "沙箱管理", icon: Container },
    ],
  },
  {
    title: "编排与自动化",
    items: [
      { to: "/workflows", label: "工作流管理", icon: GitBranch },
      { to: "/skills", label: "Skill (SOP) 管理", icon: Sparkles },
    ],
  },
  {
    title: "AIOps 运维闭环",
    items: [
      { to: "/alert-rules", label: "告警规则", icon: Bell },
      { to: "/incidents", label: "事故管理", icon: ShieldAlert },
      { to: "/evaluation", label: "评估仪表盘", icon: BarChart3 },
    ],
  },
];

export function Sidebar() {
  return (
    <aside className="flex h-screen w-64 flex-col border-r bg-sidebar text-sidebar-foreground">
      {/* Brand */}
      <div className="flex h-14 shrink-0 items-center gap-2.5 border-b px-4">
        <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <Bot className="h-4 w-4" />
        </div>
        <div className="flex flex-col leading-tight">
          <span className="text-sm font-semibold tracking-tight">CoreSRE</span>
          <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
            AIOps 智能体编排平台
          </span>
        </div>
      </div>

      {/* Grouped navigation */}
      <nav className="flex-1 overflow-y-auto px-2 py-3">
        {navSections.map((section, idx) => (
          <div
            key={section.title}
            className={cn("space-y-0.5", idx > 0 && "mt-4")}
          >
            <div className="px-3 pb-1.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground/80">
              {section.title}
            </div>
            {section.items.map(({ to, label, icon: Icon, end }) => (
              <NavLink
                key={to}
                to={to}
                end={end}
                className={({ isActive }) =>
                  cn(
                    "group relative flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                    isActive
                      ? "bg-accent text-accent-foreground"
                      : "text-muted-foreground hover:bg-accent/60 hover:text-foreground",
                  )
                }
              >
                {({ isActive }) => (
                  <>
                    {/* Active indicator bar on the left */}
                    <span
                      aria-hidden="true"
                      className={cn(
                        "absolute left-0 top-1/2 h-5 w-0.5 -translate-y-1/2 rounded-r-full bg-primary transition-opacity",
                        isActive ? "opacity-100" : "opacity-0",
                      )}
                    />
                    <Icon className="h-4 w-4 shrink-0" />
                    <span className="truncate">{label}</span>
                  </>
                )}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>

      {/* Footer / status */}
      <div className="shrink-0 border-t px-4 py-2.5 text-[10px] text-muted-foreground">
        <div className="flex items-center justify-between">
          <span>v0.1 · Demo</span>
          <span className="flex items-center gap-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
            在线
          </span>
        </div>
      </div>
    </aside>
  );
}
