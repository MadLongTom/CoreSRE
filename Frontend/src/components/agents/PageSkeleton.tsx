import { Skeleton } from "@/components/ui/skeleton";

/**
 * Full-page loading skeleton used across list, detail, and search pages.
 * Renders a header skeleton + multiple content row skeletons.
 */
export default function PageSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-6 animate-in fade-in-0 duration-300">
      {/* Title area */}
      <div className="flex items-center gap-4">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-6 w-20 rounded-full" />
      </div>

      {/* Content rows */}
      <div className="space-y-3">
        {Array.from({ length: rows }, (_, i) => (
          <div key={i} className="flex items-center gap-4">
            <Skeleton className="h-12 w-full" />
          </div>
        ))}
      </div>
    </div>
  );
}
