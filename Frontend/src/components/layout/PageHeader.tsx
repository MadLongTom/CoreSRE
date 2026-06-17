import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

interface PageHeaderProps {
  /** Page title text */
  title: string;
  /** Optional one-line description rendered below the title. */
  description?: ReactNode;
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
 * Default: a single-row bar (h-14) with bottom border. When a `description`
 * is supplied the bar grows to accommodate the subtitle while keeping the
 * title baseline aligned with the rest of the app, providing a clearer
 * visual hierarchy without breaking existing call sites.
 */
export function PageHeader({
  title,
  description,
  leading,
  actions,
  children,
  className,
}: PageHeaderProps) {
  return (
    <div
      className={cn(
        "flex shrink-0 items-center gap-3 border-b bg-background px-6",
        description ? "min-h-16 py-2" : "h-14",
        className,
      )}
    >
      {leading}
      <div className="flex min-w-0 flex-col">
        <h1 className="truncate text-lg font-semibold leading-tight">{title}</h1>
        {description && (
          <p className="truncate text-xs text-muted-foreground">{description}</p>
        )}
      </div>
      {children}
      {actions && <div className="ml-auto flex items-center gap-2">{actions}</div>}
    </div>
  );
}
