# CoreSRE

A distributed AI agent orchestration and collaboration platform built on the A2A protocol. CoreSRE serves as the **upstream orchestration layer** for enterprise AI operations — managing agent registration, discovery, scheduling, workflow execution, and multi-agent collaboration.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Frontend (React 19 + shadcn/ui)             │
│  Agent Registry · Workflow Designer · Tool Manager · Chat UI    │
├─────────────────────────────────────────────────────────────────┤
│                   API Gateway (ASP.NET Core 10)                 │
├─────────────────────────────────────────────────────────────────┤
│              Application Layer (CQRS / MediatR)                 │
├──────────┬──────────┬──────────┬──────────┬─────────────────────┤
│ Agent    │ Workflow │ Tool     │ Data     │ Sandbox             │
│ Registry │ Engine   │ Gateway  │ Sources  │ (K8s Pods)          │
├──────────┴──────────┴──────────┴──────────┴─────────────────────┤
│                      Domain Layer (DDD)                         │
├─────────────────────────────────────────────────────────────────┤
│                   Infrastructure Layer                          │
│  EF Core · A2A Protocol · MCP · External API · OTel             │
├─────────────────────────────────────────────────────────────────┤
│              .NET Aspire AppHost (Orchestration)                │
│            PostgreSQL (pgvector) · MinIO · Dashboard            │
└─────────────────────────────────────────────────────────────────┘
```

## Tech Stack

| Layer | Technology |
|-------|------------|
| **Frontend** | React 19, TypeScript 5.9, Vite 7, Tailwind CSS 4, shadcn/ui, AG-UI Protocol |
| **Backend** | .NET 10, ASP.NET Core Minimal API, MediatR (CQRS), EF Core 10 |
| **Database** | PostgreSQL 17 + pgvector (semantic search) |
| **Storage** | MinIO (S3-compatible object storage) |
| **Orchestration** | .NET Aspire (local dev), Kubernetes (production) |
| **Protocols** | A2A (Agent-to-Agent), MCP (Model Context Protocol), AG-UI |
| **Observability** | OpenTelemetry, Prometheus, Loki, Jaeger, Alertmanager |
| **AI** | Microsoft.Extensions.AI, Microsoft Agent Framework |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Node.js 22+](https://nodejs.org/) with npm
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) with Kubernetes enabled
- PostgreSQL 17 (provided by Aspire) or a running instance

### Development

```powershell
# Clone and enter the project
git clone https://github.com/MadLongTom/CoreSRE.git
cd CoreSRE

# Install frontend dependencies
cd Frontend && npm install && cd ..

# Start everything (Aspire + Frontend)
.\dev.ps1
```

### Demo Deployment (Kubernetes)

```powershell
# Deploy observability stack + demo microservices + register data sources
.\deploy-demo.ps1

# Tear down everything
.\deploy-demo.ps1 -TearDown
```

Deploys to local Docker Desktop K8s:
- **Observability** namespace: Prometheus, Loki, Jaeger, Alertmanager
- **demo-app** namespace: order/payment/inventory services + traffic generator

## Project Structure

```
CoreSRE/
├── Backend/
│   ├── CoreSRE/                    # Web API (endpoints, hubs, middleware)
│   ├── CoreSRE.AppHost/            # .NET Aspire orchestrator
│   ├── CoreSRE.Application/        # Use cases (CQRS commands & queries)
│   ├── CoreSRE.Domain/             # Entities, enums, value objects, interfaces
│   ├── CoreSRE.Infrastructure/     # EF Core, services, persistence, migrations
│   ├── CoreSRE.ServiceDefaults/    # Shared Aspire defaults (telemetry, health)
│   └── CoreSRE.*.Tests/            # Unit & integration tests
├── Frontend/                       # React SPA
├── k8s/                            # Kubernetes manifests
│   ├── demo-app/                   # Business microservices
│   └── observability/              # Monitoring stack
├── docs/                           # PRD, BRD, design docs, specs
├── specs/                          # Spec-driven development specs (per feature)
├── dev.ps1                         # One-command dev launcher
├── deploy-demo.ps1                 # K8s demo deployment script
└── CONSTITUTION.md                 # Project development constitution
```

## Key Features

- **Agent Registry** — Register, discover, and manage AI agents with semantic search (pgvector)
- **Workflow Engine** — Visual workflow designer with data-flow execution, conditional branching, and real-time SignalR push
- **Multi-Agent Chat** — Team orchestration (MagneticOne pattern) with LLM selector group chat
- **Tool Gateway** — MCP tool discovery, OpenAPI import, and dynamic tool binding
- **Data Sources** — Integrate Prometheus, Loki, Jaeger, Alertmanager, Kubernetes, and Git repositories
- **Sandbox** — Isolated code execution in Kubernetes pods with terminal access (xterm.js + WebSocket)
- **Agent Skills** — Persistent sandbox-backed skill execution for agents
- **AG-UI Protocol** — Standard streaming protocol for agent-frontend communication

## Development Principles

This project follows the [Project Constitution](CONSTITUTION.md):

1. **TDD** — Tests first, implementation second
2. **DDD** — Domain model is the single source of truth
3. **SDD** — Interface contracts before implementation

## License

Private repository. All rights reserved.
