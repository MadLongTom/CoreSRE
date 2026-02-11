import { useEffect, useCallback } from "react";
import { useBlocker } from "react-router";

/**
 * Guards against navigating away with unsaved changes.
 * - Shows browser beforeunload dialog on tab close / reload
 * - Blocks React Router navigation with a confirm prompt
 */
export function useUnsavedChangesGuard(isDirty: boolean) {
  // Browser beforeunload
  useEffect(() => {
    if (!isDirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [isDirty]);

  // React-Router navigation blocker
  const blocker = useBlocker(
    useCallback(
      ({ currentLocation, nextLocation }: { currentLocation: { pathname: string }; nextLocation: { pathname: string } }) =>
        isDirty && currentLocation.pathname !== nextLocation.pathname,
      [isDirty],
    ),
  );

  useEffect(() => {
    if (blocker.state === "blocked") {
      const confirmed = window.confirm(
        "您有未保存的更改，确定要离开吗？",
      );
      if (confirmed) {
        blocker.proceed();
      } else {
        blocker.reset();
      }
    }
  }, [blocker]);
}
