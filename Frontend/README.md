# CoreSRE Frontend

React 19 single-page application for the CoreSRE AI agent orchestration platform.

## Tech Stack

| Technology | Version | Purpose |
|-----------|---------|---------|
| React | 19 | UI framework |
| TypeScript | 5.9 | Type safety |
| Vite | 7 | Build tooling + HMR |
| Tailwind CSS | 4 | Utility-first styling |
| shadcn/ui | — | Component library |
| React Router | 7 | Client-side routing |
| React Hook Form + Zod | — | Forms & validation |
| AG-UI Protocol | 0.0.44 | Agent streaming communication |
| SignalR | 10 | Real-time workflow updates |
| Monaco Editor | — | Code editing |
| xterm.js | — | Terminal emulator (sandbox) |
| React Flow + dagre | — | Visual workflow editor |
| react-markdown | — | Markdown chat rendering |
| RxJS | — | Reactive event streams |

## Project Structure

```
src/
├── pages/                          # Route pages
│   ├── agents/                     # Agent list, create, detail, search
│   ├── chat/                       # Agent chat interface
│   ├── datasources/                # Data source list, create, detail
│   ├── providers/                  # LLM provider list, create, detail
│   ├── sandboxes/                  # Sandbox list, create, detail
│   ├── skills/                     # Skill list, create, detail
│   ├── tools/                      # Tool list, create, detail
│   └── workflows/                  # Workflow list, create, detail, execution
│
├── components/
│   ├── agents/                     # Agent-specific components
│   ├── chat/                       # Chat UI components
│   ├── datasources/                # Data source components
│   ├── layout/                     # App layout (sidebar, navbar)
│   ├── providers/                  # Provider components
│   ├── sandboxes/                  # Sandbox + terminal components
│   ├── skills/                     # Skill components
│   ├── tools/                      # Tool components
│   ├── ui/                         # shadcn/ui primitives
│   └── workflows/                  # Workflow canvas, node editors
│
├── lib/                            # Utilities, API client, helpers
├── hooks/                          # Custom React hooks
├── types/                          # TypeScript type definitions
├── App.tsx                         # Root component + routing
├── main.tsx                        # Entry point
└── index.css                       # Global styles (Tailwind)
```

## Development

### Prerequisites

- Node.js 22+
- npm

### Install & Run

```bash
npm install
npm run dev
```

Dev server starts at http://localhost:5173 with hot module replacement.

API requests are proxied to the backend at `http://localhost:5156`:

| Path | Target | Notes |
|------|--------|-------|
| `/api/chat/stream` | Backend | SSE streaming (no buffering) |
| `/api/*` | Backend | REST API + WebSocket |
| `/hubs/*` | Backend | SignalR WebSocket |
| `/health`, `/alive` | Backend | Health probes |

### Build

```bash
npm run build
```

Output in `dist/`.

### Lint

```bash
npm run lint
```
