import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

interface PageHeaderProps {
  /** Page title text */
  title: string;
  /** Optional leading element (e.g. back button) placed before the title */
  leading?: ReactNode;
  /** Optional trailing actions (e.g. buttons) placed at the end */
  actions?: ReactNode;
  /** Optional children rendered between title and actions */
  children?: ReactNode;
  /** Additional CSS classes on the root container */
  className?: string;
}

/**
 * PageHeader — unified page header bar across all pages.
 *
 * Fixed height (h-14), bottom border, horizontal padding,
 * vertically centered content. Accepts leading, title, children, and actions slots.
 */
export function PageHeader({
  title,
  leading,
  actions,
  children,
  className,
}: PageHeaderProps) {
  return (
    <div
      className={cn(
        "flex h-14 shrink-0 items-center gap-3 border-b px-6",
        className,
      )}
    >
      {leading}
      <h1 className="text-lg font-semibold">{title}</h1>
      {children}
      {actions && <div className="ml-auto flex items-center gap-2">{actions}</div>}
    </div>
  );
}
