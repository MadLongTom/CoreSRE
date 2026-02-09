# .NET Aspire — Source Code Analysis

> Focused analysis of [dotnet/aspire](https://github.com/dotnet/aspire) for building a distributed multi-agent platform.  
> Source: `e:\CoreSRE\.reference\codes\dotnet-aspire`

---

## 1. What is .NET Aspire?

**Core Value Proposition**: Aspire provides tools, templates, and packages for building **observable, production-ready distributed apps**. At the center is the **app model** — a **code-first, single source of truth** that defines your app's services, resources, and connections.

Aspire gives you a **unified toolchain**:
- **Develop**: Launch and debug your entire distributed app locally with one command
- **Observe**: Automatic OpenTelemetry, health checks, structured logs, traces, metrics via a built-in Dashboard
- **Deploy**: Same composition deploys to Kubernetes, Azure, or your own servers

**Why it matters for multi-agent platforms**: Aspire solves the exact problems we face — orchestrating multiple services/agents, wiring up their connections, observing them in real-time, and deploying them together. It's essentially a **distributed system orchestrator with built-in observability**.

---

## 2. AppHost / App Model

### 2.1 Conceptual Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    AppHost Project                       │
│  (Orchestrator — references all service projects)       │
│                                                         │
│  var builder = DistributedApplication.CreateBuilder();   │
│  var cache   = builder.AddRedis("cache");               │
│  var db      = builder.AddPostgres("pg").AddDatabase(); │
│  var api     = builder.AddProject<Projects.Api>("api")  │
│                  .WithReference(cache)                   │
│                  .WithReference(db)                      │
│                  .WaitFor(db);                           │
│  builder.Build().Run();                                  │
└─────────────────────────────────────────────────────────┘
```

### 2.2 How the AppHost Works

The AppHost is a **special .NET project** that uses the `Aspire.Hosting.AppHost` SDK. When you add a `<ProjectReference>` to another project, the SDK's MSBuild targets **auto-generate a metadata class** (`Projects.MyApp`) implementing `IProjectMetadata`, which carries the path to the referenced `.csproj`. At runtime, Aspire parses each referenced project's `launchSettings.json` to discover endpoints and launch profiles.

**Key flow:**
1. `DistributedApplication.CreateBuilder(args)` creates an `IDistributedApplicationBuilder`
2. Extension methods (`AddProject`, `AddRedis`, `AddContainer`, etc.) add resources to `builder.Resources` (an `IResourceCollection`)
3. `WithReference()` / `WaitFor()` wire up dependencies between resources (connection strings, service discovery, ordering)
4. `builder.Build()` validates the model (no duplicate names), builds an `IHost`, and returns a `DistributedApplication`
5. `.Run()` starts the DCP (Developer Control Plane) orchestrator, which launches containers, projects, and executables, allocates endpoints, and starts the Dashboard

### 2.3 `IDistributedApplicationBuilder` — Central Interface

Source: `src/Aspire.Hosting/IDistributedApplicationBuilder.cs`

```csharp
public interface IDistributedApplicationBuilder
{
    ConfigurationManager Configuration { get; }    // Standard .NET config
    string AppHostDirectory { get; }                // AppHost project directory
    Assembly? AppHostAssembly { get; }              // AppHost assembly (for metadata)
    IHostEnvironment Environment { get; }           // Dev/Staging/Prod
    IServiceCollection Services { get; }            // DI container
    IDistributedApplicationEventing Eventing { get; } // Pub/sub lifecycle events
    DistributedApplicationExecutionContext ExecutionContext { get; } // Run vs Publish mode
    IResourceCollection Resources { get; }          // All registered resources
    
    IResourceBuilder<T> AddResource<T>(T resource) where T : IResource;
    IResourceBuilder<T> CreateResourceBuilder<T>(T resource) where T : IResource;
    DistributedApplication Build();
}
```

### 2.4 The Resource Model

Everything in the app model is an `IResource`:

```csharp
public interface IResource
{
    string Name { get; }
    ResourceAnnotationCollection Annotations { get; }
}
```

Resources are configured through the **builder pattern** via `IResourceBuilder<T>`:

```csharp
public interface IResourceBuilder<out T> where T : IResource
{
    IDistributedApplicationBuilder ApplicationBuilder { get; }
    T Resource { get; }
    IResourceBuilder<T> WithAnnotation<TAnnotation>(TAnnotation annotation, ...);
}
```

**Key resource interfaces:**
| Interface | Purpose |
|---|---|
| `IResourceWithEndpoints` | Has network endpoints (HTTP, gRPC, TCP) |
| `IResourceWithServiceDiscovery` | Extends endpoints — supports service discovery injection |
| `IResourceWithConnectionString` | Provides a connection string (databases, caches) |
| `IResourceWithEnvironment` | Can receive environment variables |
| `IResourceWithArgs` | Can receive command-line arguments |
| `IResourceWithWaitSupport` | Supports `WaitFor()` ordering |

### 2.5 Execution Context: Run vs Publish

```csharp
ExecutionContext.IsRunMode     // Local dev — launch containers, projects, dashboard
ExecutionContext.IsPublishMode // Generate deployment manifests (K8s, Azure, etc.)
```

This lets you conditionally configure the model — e.g., use a container for Postgres in dev but a managed service in production.

---

## 3. Service Defaults

Source: `src/Aspire.ProjectTemplates/templates/aspire-servicedefaults/Extensions.cs`

ServiceDefaults is a **shared project** referenced by every service in your Aspire solution. It provides opinionated defaults for cross-cutting concerns via a single `AddServiceDefaults()` call:

```csharp
public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) 
    where TBuilder : IHostApplicationBuilder
{
    builder.ConfigureOpenTelemetry();       // OTel traces, metrics, logs
    builder.AddDefaultHealthChecks();       // /health and /alive endpoints
    builder.Services.AddServiceDiscovery(); // Automatic service discovery
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler(); // Polly resilience (retry, circuit breaker)
        http.AddServiceDiscovery();          // HttpClient automatically resolves service names
    });
    return builder;
}
```

### What ServiceDefaults wires up:

| Concern | What it does |
|---|---|
| **OpenTelemetry** | Logging (formatted + scoped), Metrics (ASP.NET, HTTP, Runtime), Tracing (ASP.NET, HTTP) |
| **OTLP Export** | Auto-detects `OTEL_EXPORTER_OTLP_ENDPOINT` → exports to Aspire Dashboard |
| **Health Checks** | `/health` (readiness) and `/alive` (liveness) — only in Development |
| **Service Discovery** | `Microsoft.Extensions.ServiceDiscovery` — resolves `http://myservice` to actual endpoints |
| **Resilience** | `AddStandardResilienceHandler()` — Polly retry, circuit-breaker, timeout, hedging |

### NuGet Dependencies:
```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" />
<PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
```

---

## 4. Aspire Dashboard

Source: `src/Aspire.Dashboard/`

### What it provides:

The Aspire Dashboard is a **Blazor Server** web application that displays:

1. **Resources**: All services, containers, and executables in your app — their state, endpoints, environment
2. **Console Logs**: Live streaming of stdout/stderr from every resource
3. **Structured Logs**: OpenTelemetry log records with structured data, filtering, search
4. **Traces**: Distributed traces spanning multiple services — waterfall view
5. **Metrics**: Runtime and application metrics with graphs

### How it integrates:

```
┌──────────────┐      OTLP (gRPC/HTTP)       ┌─────────────────┐
│  Service A   │ ──────────────────────────→  │  Aspire Dashboard│
│  Service B   │ ──────────────────────────→  │  (Blazor UI)     │
│  Container C │ ──────────────────────────→  │                   │
└──────────────┘                              │  :18888 (UI)      │
                                              │  :18889 (OTLP gRPC)│
       ┌──────────────┐    gRPC Resource Svc  │  :18890 (OTLP HTTP)│
       │  AppHost     │ ──────────────────→   └─────────────────┘
       │  (DCP)       │    (resource state,
       └──────────────┘     logs, commands)
```

- **DashboardServiceHost** in the AppHost runs a gRPC "Resource Service" that streams resource state, logs, and accepts commands (start/stop/restart)
- The Dashboard connects to this Resource Service to display resource info
- Each service sends telemetry directly to the Dashboard's OTLP endpoints
- Dashboard supports **authentication**: Browser Token, OIDC, or Unsecured (dev only)
- Dashboard supports **MCP (Model Context Protocol)** integration for AI assistant access to resource data

### Key Configuration:
| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_URLS` | `http://localhost:18888` | Dashboard UI |
| `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL` | `http://localhost:18889` | OTLP/gRPC receiver |
| `DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` | `http://localhost:18890` | OTLP/HTTP receiver |

---

## 5. Component / Integration Model

### 5.1 Two sides of integrations:

| Side | Package Pattern | Purpose |
|---|---|---|
| **Hosting** | `Aspire.Hosting.{Tech}` | AppHost — adds resource to the model (container/cloud service) |
| **Client** | `Aspire.{Client.Lib}` | Service project — configures SDK client + health checks + telemetry |

### 5.2 Adding Resources — Hosting Extensions

```csharp
// Databases
var pg = builder.AddPostgres("pg").AddDatabase("mydb");
var sql = builder.AddSqlServer("sql").AddDatabase("mydb");
var mongo = builder.AddMongoDB("mongo").AddDatabase("mydb");
var cosmos = builder.AddAzureCosmosDB("cosmos").AddDatabase("mydb");
var mysql = builder.AddMySql("mysql").AddDatabase("mydb");

// Caches
var redis = builder.AddRedis("cache");
var garnet = builder.AddGarnet("cache");
var valkey = builder.AddValkey("cache");

// Messaging
var rabbit = builder.AddRabbitMQ("messaging");
var kafka = builder.AddKafka("kafka");
var nats = builder.AddNats("nats");
var sb = builder.AddAzureServiceBus("sb");
var eh = builder.AddAzureEventHubs("eh");

// AI / ML
var openai = builder.AddAzureOpenAI("openai");
var github = builder.AddGitHubModels("github-models");

// Search / Vector
var qdrant = builder.AddQdrant("qdrant");
var milvus = builder.AddMilvus("milvus");
var search = builder.AddAzureSearch("search");

// Storage
var storage = builder.AddAzureStorage("storage");
var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");

// Observability
var seq = builder.AddSeq("seq");

// Containers (custom)
var container = builder.AddContainer("mycontainer", "myimage", "latest")
    .WithHttpEndpoint(port: 8080, targetPort: 80)
    .WithEnvironment("KEY", "value");
```

### 5.3 Key Integration Types in the Repo:

```
src/Aspire.Hosting.Redis/        src/Components/Aspire.StackExchange.Redis/
src/Aspire.Hosting.PostgreSQL/   src/Components/Aspire.Npgsql/
src/Aspire.Hosting.SqlServer/    src/Components/Aspire.Microsoft.Data.SqlClient/
src/Aspire.Hosting.MongoDB/      src/Components/Aspire.MongoDB.Driver/
src/Aspire.Hosting.RabbitMQ/     src/Components/Aspire.RabbitMQ.Client/
src/Aspire.Hosting.Kafka/        src/Components/Aspire.Confluent.Kafka/
src/Aspire.Hosting.Nats/         src/Components/Aspire.NATS.Net/
src/Aspire.Hosting.Qdrant/       src/Components/Aspire.Qdrant.Client/
src/Aspire.Hosting.OpenAI/       src/Components/Aspire.OpenAI/
src/Aspire.Hosting.Kubernetes/   (deployment target, not a resource)
```

### 5.4 Polyglot Support:

Aspire supports non-.NET workloads via code generation extensions:
```
src/Aspire.Hosting.Python/
src/Aspire.Hosting.JavaScript/
src/Aspire.Hosting.CodeGeneration.Go/
src/Aspire.Hosting.CodeGeneration.Java/
src/Aspire.Hosting.CodeGeneration.TypeScript/
src/Aspire.Hosting.CodeGeneration.Rust/
```

---

## 6. Key APIs

### 6.1 `IDistributedApplicationBuilder`

Central builder interface. Created via:
```csharp
var builder = DistributedApplication.CreateBuilder(args);
```

Exposes `Configuration`, `Services` (DI), `Environment`, `ExecutionContext`, `Resources`, `Eventing`.

### 6.2 `AddProject<T>()`

```csharp
builder.AddProject<Projects.MyService>("myservice");                  // By generated type
builder.AddProject("myservice", "../MyService/MyService.csproj");     // By path
builder.AddProject<Projects.MyService>("myservice", "otherProfile");  // Custom launch profile
```

The `TProject` type is **auto-generated by MSBuild** when you add a `<ProjectReference>` in the AppHost `.csproj`. It implements `IProjectMetadata` with the project path, allowing Aspire to parse launch settings and configure endpoints.

### 6.3 `AddContainer()`

```csharp
builder.AddContainer("mycontainer", "myimage");            // "latest" tag
builder.AddContainer("mycontainer", "myimage", "1.0");     // Specific tag
```

Returns `IResourceBuilder<ContainerResource>`. Supports `.WithImage()`, `.WithVolume()`, `.WithBindMount()`, `.WithEnvironment()`, etc.

### 6.4 `AddConnectionString()`

```csharp
builder.AddConnectionString("mydb");
// Reads from configuration: ConnectionStrings:mydb
// Returns IResourceBuilder<IResourceWithConnectionString>
```

Used when you have an existing connection string (e.g., from cloud) rather than an Aspire-managed resource.

### 6.5 `WithReference()`

**The core wiring API.** Overloads:

```csharp
// Connection string injection → ConnectionStrings__source in dest's env
.WithReference(sourceWithConnectionString)
.WithReference(sourceWithConnectionString, connectionName: "custom")

// Service discovery injection → services__source__endpoint__0=uri in dest's env
.WithReference(sourceWithServiceDiscovery)

// Endpoint reference → injects specific endpoint
.WithReference(source.GetEndpoint("http"))

// External service
.WithReference(externalService)

// URI-based
.WithReference("name", new Uri("http://external:8080/"))
```

How connection strings flow:
```csharp
var db = builder.AddPostgres("pg").AddDatabase("inventory");
builder.AddProject<Projects.Api>("api").WithReference(db);
// → Api receives env var: ConnectionStrings__inventory=Host=...;Port=5432;...
```

### 6.6 `WithEndpoint()`

```csharp
.WithEndpoint(port: 8080, targetPort: 80, scheme: "http", name: "web")
.WithHttpEndpoint(port: 8080, targetPort: 80)
.WithHttpsEndpoint(port: 8443, targetPort: 443)
.WithEndpoint("myendpoint", e => { e.Port = 9090; e.IsExternal = true; })
```

### 6.7 `WaitFor()`

Declares startup ordering:
```csharp
builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WaitFor(db);       // Don't start api until db is healthy
```

### 6.8 Service Discovery Pattern

In the **AppHost** (orchestrator side):
```csharp
var api = builder.AddProject<Projects.Api>("api");
var web = builder.AddProject<Projects.Web>("web").WithReference(api);
// → web receives: services__api__http__0=http://localhost:5123
```

In the **service project** (client side, via ServiceDefaults):
```csharp
builder.Services.AddHttpClient<ApiClient>(c => c.BaseAddress = new("http://api"));
// "http://api" is resolved by ServiceDiscovery to the actual endpoint
```

The environment variable format is: `services__{name}__{endpointName}__{index}={uri}`

### 6.9 Eventing System

```csharp
builder.Eventing.Subscribe<BeforeStartEvent>(async (e, ct) => { ... });
builder.Eventing.Subscribe<AfterEndpointsAllocatedEvent>(async (e, ct) => { ... });
builder.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (e, ct) => { ... });
```

Supports `BlockingSequential`, `BlockingConcurrent`, `NonBlockingSequential`, `NonBlockingConcurrent` dispatch behaviors.

---

## 7. Deployment / Kubernetes

### 7.1 Publish Mode

Aspire separates **Run** (local dev) from **Publish** (deployment):

```csharp
if (builder.ExecutionContext.IsPublishMode) { ... }   // generating manifests
if (builder.ExecutionContext.IsRunMode) { ... }        // local dev
```

### 7.2 Manifest Publishing

The default publisher generates a JSON **manifest** describing the app model:
```shell
aspire publish -o ./output
```

This manifest is consumed by deployment tools (Azure Developer CLI, custom publishers).

### 7.3 Kubernetes Publishing

Package: `Aspire.Hosting.Kubernetes`

```csharp
builder.AddKubernetesEnvironment("k8s");
```
```shell
aspire publish -o k8s-artifacts
```

The `KubernetesInfrastructure` class generates Kubernetes manifests:
- Helm charts with the app name
- Deployments, Services, ConfigMaps, Secrets
- YAML output in `k8s-artifacts/`

Customizable via:
```csharp
builder.AddKubernetesEnvironment("k8s").WithProperties(k8s =>
{
    k8s.HelmChartName = "my-chart";
});
```

### 7.4 Pipeline System

Publishing uses a **PipelineStep** model with dependency ordering:

```csharp
// Well-known steps: Build → Push → Publish
// Steps can declare DependsOn and RequiredBy relationships
```

Source: `src/Aspire.Hosting/Publishing/PipelineExecutor.cs` and `src/Aspire.Hosting/Pipelines/`

---

## 8. OpenTelemetry Integration

### 8.1 AppHost Side (Orchestrator → Services)

Source: `src/Aspire.Hosting/OtlpConfigurationExtensions.cs`

When the AppHost launches resources, it injects OTLP environment variables:

```csharp
// Injected into every resource with OtlpExporterAnnotation:
OTEL_EXPORTER_OTLP_ENDPOINT = <dashboard-otlp-endpoint>
OTEL_EXPORTER_OTLP_HEADERS  = x-otlp-api-key=<generated-key>
OTEL_SERVICE_NAME            = <resource-name>
OTEL_RESOURCE_ATTRIBUTES     = service.instance.id=<uid>

// Development optimizations:
OTEL_BLRP_SCHEDULE_DELAY     = 1000   // Batch log export delay (ms)
OTEL_BSP_SCHEDULE_DELAY      = 1000   // Batch span export delay (ms)
OTEL_METRIC_EXPORT_INTERVAL  = 1000   // Metric export interval (ms)
OTEL_TRACES_SAMPLER           = always_on
OTEL_METRICS_EXEMPLAR_FILTER  = trace_based

// GenAI telemetry:
OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT = true
```

### 8.2 Service Side (ServiceDefaults)

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddRuntimeInstrumentation();
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource(builder.Environment.ApplicationName)
               .AddAspNetCoreInstrumentation(t => t.Filter = /* exclude health checks */)
               .AddHttpClientInstrumentation();
    });

// Auto-detects OTEL_EXPORTER_OTLP_ENDPOINT and exports
builder.Services.AddOpenTelemetry().UseOtlpExporter();
```

### 8.3 End-to-End Flow

```
┌─────────────┐                    ┌───────────────────┐
│  Service     │  OTLP gRPC/HTTP   │  Aspire Dashboard │
│  (with       │ ──────────────→   │                   │
│   OTel SDK)  │  traces,metrics,  │  Stores in-memory │
│              │  logs             │  Renders Blazor UI│
└─────────────┘                    └───────────────────┘
       ↑
       │ Env vars injected by AppHost:
       │ OTEL_EXPORTER_OTLP_ENDPOINT
       │ OTEL_SERVICE_NAME
       │ OTEL_EXPORTER_OTLP_HEADERS
```

### 8.4 Key OTel Signals:

| Signal | Source Instrumentation | Dashboard View |
|---|---|---|
| **Traces** | ASP.NET Core, HttpClient, custom ActivitySource | Distributed trace waterfall |
| **Metrics** | ASP.NET Core (requests), HttpClient (outgoing), Runtime (.NET GC, threads) | Time-series graphs |
| **Logs** | `ILogger` → OpenTelemetry log exporter | Structured log viewer with filtering |

---

## Summary: Relevance to Multi-Agent Platform

| Aspire Concept | Our Use Case |
|---|---|
| **AppHost** | Orchestrate multiple agent services, databases, message buses |
| **App Model** (`AddProject`, `AddContainer`) | Define agents as projects/containers, define infra (Redis, Postgres, RabbitMQ) |
| **`WithReference`** | Wire agent → database, agent → message bus, agent → another agent |
| **`WaitFor`** | Ensure database/bus is ready before agents start |
| **Service Discovery** | Agents find each other by name (`http://agent-planner`) |
| **ServiceDefaults** | Every agent gets OTel, health checks, resilience, service discovery for free |
| **Dashboard** | Real-time visibility into all agents: logs, traces across agent calls, metrics |
| **OTel Integration** | End-to-end distributed tracing across multi-agent conversations |
| **Eventing** | Hook into lifecycle events (before start, after endpoints allocated) |
| **Kubernetes Publishing** | Deploy the entire agent constellation to K8s with one command |
| **Parameters** | Externalize API keys, model endpoints as `AddParameter(secret: true)` |
