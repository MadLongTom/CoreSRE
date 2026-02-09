import { useEffect, useState } from "react";

interface HealthStatus {
  status: string;
  checkedAt: string;
}

function App() {
  const [health, setHealth] = useState<HealthStatus | null>(null);

  useEffect(() => {
    fetch("/health")
      .then((res) => ({
        status: res.ok ? "Healthy" : "Unhealthy",
        checkedAt: new Date().toISOString(),
      }))
      .then(setHealth)
      .catch(console.error);
  }, []);

  return (
    <div className="min-h-screen bg-background text-foreground flex items-center justify-center">
      <div className="text-center space-y-6">
        <h1 className="text-4xl font-bold">CoreSRE</h1>
        <p className="text-muted-foreground">Full-Stack SRE Platform</p>
        <div className="rounded-lg border p-4 text-sm">
          <p className="font-medium">Backend Health Check:</p>
          {health ? (
            <p className="text-green-600">
              ✅ {health.status} — {health.checkedAt}
            </p>
          ) : (
            <p className="text-yellow-600">⏳ Connecting...</p>
          )}
        </div>
      </div>
    </div>
  );
}

export default App;
