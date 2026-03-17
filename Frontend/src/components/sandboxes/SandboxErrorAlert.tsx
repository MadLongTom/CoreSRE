import { AlertTriangle } from "lucide-react";

export interface SandboxError {
  message: string;
  hints?: string[];
}

interface SandboxErrorAlertProps {
  error: SandboxError;
  className?: string;
}

/**
 * Displays a sandbox operation error with optional actionable hints.
 * Backend returns structured 422 responses with a message and a list of
 * troubleshooting hints that map to `ApiError.errors`.
 */
export function SandboxErrorAlert({ error, className }: SandboxErrorAlertProps) {
  return (
    <div
      className={`rounded-lg border border-destructive/30 bg-destructive/5 p-4 space-y-3 ${className ?? ""}`}
    >
      {/* Main error message */}
      <div className="flex items-start gap-2 text-sm font-medium text-destructive">
        <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
        <span>{error.message}</span>
      </div>

      {/* Actionable hints */}
      {error.hints && error.hints.length > 0 && (
        <div className="ml-6 space-y-1">
          <p className="text-xs font-medium text-muted-foreground">排查建议：</p>
          <ul className="list-disc list-inside space-y-1 text-xs text-muted-foreground">
            {error.hints.map((hint, i) => (
              <li key={i} className="leading-relaxed">
                {hint}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
