# CoreSRE Backend

ASP.NET Core 10 backend following **Clean Architecture** (DDD) with CQRS pattern via MediatR.

## Solution Structure

```
Backend/
├── CoreSRE/                        # Web API layer
│   ├── Endpoints/                  # Minimal API endpoint definitions
│   ├── Hubs/                       # SignalR hubs (WorkflowHub)
│   ├── Middleware/                  # AG-UI, error handling
│   └── Program.cs                  # Application entry point
│
├── CoreSRE.AppHost/                # .NET Aspire orchestrator
│   └── Program.cs                  # PostgreSQL + MinIO + API wiring
│
├── CoreSRE.Application/            # Application layer (use cases)
│   ├── Agents/                     # Agent CRUD, search, team orchestration
│   ├── Chat/                       # Conversation & message management
│   ├── DataSources/                # Data source registration & queries
│   ├── Providers/                  # LLM provider configuration
│   ├── Sandboxes/                  # Sandbox lifecycle management
│   ├── Skills/                     # Agent skill CRUD
│   ├── Tools/                      # Tool registry & MCP discovery
│   └── Workflows/                  # Workflow CRUD & execution engine
│
├── CoreSRE.Domain/                 # Domain layer (zero dependencies)
│   ├── Entities/                   # 12 aggregate roots / entities
│   ├── Enums/                      # 20 domain enumerations
│   ├── Interfaces/                 # Repository & service contracts
│   └── ValueObjects/               # Immutable value objects
│
├── CoreSRE.Infrastructure/         # Infrastructure layer
│   ├── Persistence/                # EF Core DbContext, migrations, repositories
│   └── Services/                   # External integrations
│       ├── DataSources/            # Prometheus, Loki, Jaeger, K8s, Git queriers
│       ├── Sandbox/                # Kubernetes pod sandbox management
│       ├── Storage/                # MinIO file storage
│       └── ...                     # MCP, A2A, workflow engine, team orchestrator
│
├── CoreSRE.ServiceDefaults/        # Shared Aspire defaults (OpenTelemetry, health)
├── CoreSRE.Application.Tests/      # Application layer unit tests
└── CoreSRE.Infrastructure.Tests/   # Infrastructure layer unit tests
```

## API Endpoints

| Endpoint Group | Path Prefix | Description |
|---------------|-------------|-------------|
| Agents | `/api/agents` | Agent CRUD, semantic search, team management |
| Chat | `/api/agents/{id}/chat`, `/api/conversations` | Agent chat, conversation history |
| Data Sources | `/api/datasources` | Data source registration, health, queries |
| Providers | `/api/providers` | LLM provider & model configuration |
| Sandboxes | `/api/sandboxes` | Sandbox CRUD, pod lifecycle |
| Skills | `/api/skills` | Agent skill management |
| Tools | `/api/tools` | Tool registry, MCP discovery, OpenAPI import |
| Workflows | `/api/workflows` | Workflow CRUD, execution, real-time status |
| Files | `/api/files` | File upload/download (MinIO) |
| Webhooks | `/api/webhooks` | External webhook handlers |

**SignalR Hub**: `/hubs/workflow` — Real-time workflow execution push

**AG-UI**: `/api/chat/stream` — AG-UI protocol SSE streaming

## Key Infrastructure Services

| Service | Purpose |
|---------|---------|
| `WorkflowEngine` | Data-flow workflow execution with V8 expression evaluator |
| `TeamOrchestratorService` | Multi-agent collaboration (MagneticOne pattern) |
| `McpToolDiscoveryService` | MCP protocol tool discovery & invocation |
| `A2ACardResolverService` | A2A agent card resolution |
| `KubernetesQuerier` | K8s cluster resource queries |
| `PrometheusQuerier` | PromQL queries to Prometheus |
| `LokiQuerier` | LogQL queries to Loki |
| `JaegerQuerier` | Trace queries to Jaeger |
| `SandboxPodPool` | Kubernetes pod lifecycle management for sandboxes |

## Development

### Prerequisites

- .NET 10 SDK
- Docker Desktop (for Aspire-managed PostgreSQL + MinIO)

### Run with Aspire

```powershell
dotnet run --project CoreSRE.AppHost
```

This starts PostgreSQL (pgvector), MinIO, and the API. Aspire Dashboard at https://localhost:17178.

### Run API only

```powershell
dotnet run --project CoreSRE
```

Requires a PostgreSQL connection string in `appsettings.Development.json`.

### Run Tests

```powershell
dotnet test
```

### Database Migrations

```powershell
# Add migration
dotnet ef migrations add <Name> --project CoreSRE.Infrastructure --startup-project CoreSRE

# Apply migrations (auto-applied on startup in Development)
dotnet ef database update --project CoreSRE.Infrastructure --startup-project CoreSRE
```

## Configuration

Key settings in `appsettings.json` / `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "coresre": "Host=localhost;Database=coresre;Username=postgres;Password=..."
  }
}
```

When running via Aspire AppHost, connection strings are injected automatically.
