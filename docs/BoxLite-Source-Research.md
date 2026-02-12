# BoxLite Source Code Research

> **Purpose**: Complete API surface documentation for building a .NET client that talks to BoxLite VMs.
> **Source**: `.reference/codes/boxlite/` (v0.5.10)

---

## 1. Architecture Overview

BoxLite is an **embeddable micro-VM runtime** — "SQLite for sandboxing". It runs OCI containers inside lightweight VMs with hardware-level isolation (KVM on Linux, Hypervisor.framework on macOS). **No daemon required.**

```
Host Application
  └─ BoxliteRuntime (Rust library, embedded)
       └─ LiteBox (per-VM handle)
            └─ ShimController (spawns boxlite-shim subprocess)
                 └─ Jailer Boundary (seccomp/sandbox-exec)
                      └─ libkrun VM (KVM/HVF)
                           └─ Guest Agent (gRPC server, listens on vsock)
                                └─ OCI Container (libcontainer)
```

### Key Architecture Points

- **No separate server process** — BoxLite is a **library** linked into the host application
- SDKs (Python/Node.js) are **native bindings** (PyO3/napi-rs) wrapping the Rust core
- The host communicates with the guest VM via **gRPC over vsock** (bridged through Unix socket by libkrun)
- There is **no REST/HTTP API** exposed by the runtime itself — all communication is via the native Rust API or the gRPC protocol to the guest

---

## 2. Communication Protocol — gRPC over vsock

### Transport Flow

```
Host Application
      │
      │ Unix Socket (/tmp/boxlite-{id}.sock)
      ▼
┌─────────────────┐
│  libkrun vsock  │  (Unix socket ↔ vsock bridge)
│     bridge      │
└─────────────────┘
      │
      │ Vsock (port 2695)
      ▼
Guest Agent (gRPC Server on vsock://2695)
```

### Transport Types (from `boxlite-shared/src/transport.rs`)

```rust
enum Transport {
    Tcp { port: u16 },          // tcp://127.0.0.1:{port}
    Unix { socket_path: PathBuf }, // unix://{path}
    Vsock { port: u32 },        // vsock://{port}
}
```

- **Host side**: Connects via **Unix socket** (`unix:///tmp/boxlite-{id}.sock`)
- **libkrun**: Bridges Unix socket ↔ vsock automatically
- **Guest side**: Listens on **vsock port 2695** (`vsock://2695`)
- **Guest ready signal**: Guest connects to vsock port **2696** to notify host it's ready

### Key Constants (`boxlite-shared/src/constants.rs`)

| Constant | Value | Description |
|----------|-------|-------------|
| `GUEST_AGENT_PORT` | `2695` | vsock port for guest gRPC server |
| `GUEST_READY_PORT` | `2696` | vsock port for guest ready notification |
| `DEFAULT_HOSTNAME` | `"boxlite"` | Container hostname |

### Network Constants (`boxlite/src/net/constants.rs`)

| Constant | Value | Description |
|----------|-------|-------------|
| `SUBNET` | `192.168.127.0/24` | Virtual network subnet |
| `GATEWAY_IP` | `192.168.127.1` | gvproxy gateway / DNS server |
| `GUEST_IP` | `192.168.127.2` | Guest VM IP (static DHCP) |
| `GUEST_MAC` | `5a:94:ef:e4:0c:ee` | Guest MAC address |
| `GATEWAY_MAC` | `5a:94:ef:e4:0c:dd` | Gateway MAC address |
| `DEFAULT_MTU` | `1500` | Network MTU |

---

## 3. Proto/gRPC Definitions — COMPLETE

**File**: `boxlite-shared/proto/boxlite/v1/service.proto`
**Package**: `boxlite.v1`

### Services (4 total)

#### 3.1 `service Guest` — Guest Agent Management

| RPC Method | Request | Response | Streaming | Description |
|------------|---------|----------|-----------|-------------|
| `Init` | `GuestInitRequest` | `GuestInitResponse` | Unary | Initialize guest environment (mounts, network). **MUST be called first.** |
| `Ping` | `PingRequest` | `PingResponse` | Unary | Health check, returns guest agent version |
| `Shutdown` | `ShutdownRequest` | `ShutdownResponse` | Unary | Graceful shutdown |

#### 3.2 `service Container` — Container Lifecycle

| RPC Method | Request | Response | Streaming | Description |
|------------|---------|----------|-----------|-------------|
| `Init` | `ContainerInitRequest` | `ContainerInitResponse` | Unary | Initialize OCI container (after Guest.Init). Prepares rootfs, starts container. |

#### 3.3 `service Execution` — Command Execution

| RPC Method | Request | Response | Streaming | Description |
|------------|---------|----------|-----------|-------------|
| `Exec` | `ExecRequest` | `ExecResponse` | Unary | Start execution immediately |
| `Attach` | `AttachRequest` | `stream ExecOutput` | Server streaming | Attach to stdout/stderr (output only) |
| `SendInput` | `stream ExecStdin` | `SendInputAck` | Client streaming | Send stdin data |
| `Wait` | `WaitRequest` | `WaitResponse` | Unary | Wait for execution to complete (blocking) |
| `Kill` | `KillRequest` | `KillResponse` | Unary | Kill execution (send signal) |
| `ResizeTty` | `ResizeTtyRequest` | `ResizeTtyResponse` | Unary | Resize PTY window (PTY sessions only) |

#### 3.4 `service Files` — File Transfer

| RPC Method | Request | Response | Streaming | Description |
|------------|---------|----------|-----------|-------------|
| `Upload` | `stream UploadChunk` | `UploadResponse` | Client streaming | Upload tar archive, extract at dest_path |
| `Download` | `DownloadRequest` | `stream DownloadChunk` | Server streaming | Download path as tar archive |

### All Message Types

#### Guest Service Messages

```protobuf
message GuestInitRequest {
  repeated Volume volumes = 1;     // virtiofs + block devices
  NetworkInit network = 2;         // optional network config
}

message GuestInitResponse {
  oneof result {
    GuestInitSuccess success = 1;
    GuestInitError error = 2;
  }
}

message GuestInitSuccess {}
message GuestInitError { string reason = 1; }

message Volume {
  string mount_point = 1;
  oneof source {
    VirtiofsSource virtiofs = 2;
    BlockDeviceSource block_device = 3;
  }
  string container_id = 4;        // optional, for convention-based paths
}

message VirtiofsSource {
  string tag = 1;
  bool read_only = 2;
}

message BlockDeviceSource {
  string device = 1;              // e.g., "/dev/vda"
  Filesystem filesystem = 2;     // only EXT4 supported
  bool need_format = 3;
  bool need_resize = 4;
}

enum Filesystem {
  FILESYSTEM_UNSPECIFIED = 0;
  FILESYSTEM_EXT4 = 1;
}

message RootfsInit {
  oneof strategy {
    MergedRootfs merged = 1;
    OverlayRootfs overlay = 2;
    DiskRootfs disk = 3;
  }
}

message MergedRootfs {}

message OverlayRootfs {
  repeated string layer_names = 1;
  bool copy_layers = 2;
}

message DiskRootfs {
  string device = 1;
  bool need_format = 2;
  bool need_resize = 3;
}

message NetworkInit {
  string interface = 1;           // e.g., "eth0"
  optional string ip = 2;        // e.g., "192.168.127.2/24"
  optional string gateway = 3;   // e.g., "192.168.127.1"
}

message PingRequest {}
message PingResponse { string version = 1; }
message ShutdownRequest {}
message ShutdownResponse {}
```

#### Container Service Messages

```protobuf
message ContainerInitRequest {
  string container_id = 1;
  ContainerConfig container_config = 2;
  RootfsInit rootfs = 3;
  repeated BindMount mounts = 4;
}

message BindMount {
  string volume_name = 1;         // used to construct guest path
  string destination = 2;         // e.g., "/data"
  bool read_only = 3;
}

message ContainerInitResponse {
  oneof result {
    ContainerInitSuccess success = 1;
    ContainerInitError error = 2;
  }
}

message ContainerInitSuccess { string container_id = 1; }
message ContainerInitError { string reason = 1; }

message ContainerConfig {
  repeated string entrypoint = 1;  // e.g., ["/bin/sh", "-c", "echo hello"]
  repeated string env = 2;         // e.g., ["PATH=/usr/bin", "HOME=/root"]
  string workdir = 3;              // e.g., "/app"
  string user = 4;                 // e.g., "0:0" or "root"
}
```

#### Execution Service Messages

```protobuf
message ExecRequest {
  optional string execution_id = 1;
  string program = 2;
  repeated string args = 3;
  map<string, string> env = 4;
  string workdir = 5;
  uint64 timeout_ms = 6;
  optional TtyConfig tty = 7;     // if set, use PTY
}

message TtyConfig {
  uint32 rows = 1;
  uint32 cols = 2;
  uint32 x_pixels = 3;
  uint32 y_pixels = 4;
}

message ExecResponse {
  string execution_id = 1;
  uint32 pid = 2;
  uint64 started_at_ms = 3;
  optional ExecError error = 4;
}

message ExecError {
  string reason = 1;
  string detail = 2;
}

message AttachRequest { string execution_id = 1; }

message ExecOutput {
  oneof event {
    Stdout stdout = 1;
    Stderr stderr = 2;
  }
}

message Stdout { bytes data = 1; }
message Stderr { bytes data = 1; }

message ExecStdin {
  string execution_id = 1;
  bytes data = 2;
  bool close = 3;
}

message SendInputAck {}

message WaitRequest { string execution_id = 1; }

message WaitResponse {
  int32 exit_code = 1;
  int32 signal = 2;
  bool timed_out = 3;
  uint64 duration_ms = 4;
  string error_message = 5;
}

message KillRequest {
  string execution_id = 1;
  int32 signal = 2;               // default: 9 = SIGKILL
}

message KillResponse {
  bool success = 1;
  optional string error = 2;
}

message ResizeTtyRequest {
  string execution_id = 1;
  uint32 rows = 2;
  uint32 cols = 3;
  uint32 x_pixels = 4;
  uint32 y_pixels = 5;
}

message ResizeTtyResponse {
  bool success = 1;
  optional string error = 2;
}
```

#### Files Service Messages

```protobuf
message UploadChunk {
  string dest_path = 1;           // first chunk MUST include this
  string container_id = 2;        // optional
  bytes data = 3;                 // raw tar bytes
  bool mkdir_parents = 4;         // default: true
  bool overwrite = 5;             // default: true
}

message UploadResponse {
  bool success = 1;
  optional string error = 2;
}

message DownloadRequest {
  string src_path = 1;
  string container_id = 2;
  bool include_parent = 3;
  bool follow_symlinks = 4;
}

message DownloadChunk { bytes data = 1; }
```

---

## 4. Initialization Sequence (Host → Guest)

This is the exact sequence a .NET client must follow:

```
1. Spawn VM (libkrun subprocess)
2. Wait for guest ready signal (vsock port 2696)
3. Connect gRPC channel to guest (unix socket → vsock 2695)

4. Guest.Init(GuestInitRequest)
   - volumes: virtiofs mounts + block devices
   - network: interface, IP, gateway
   → Returns GuestInitResponse

5. Container.Init(ContainerInitRequest)
   - container_id: host-generated UUID
   - container_config: entrypoint, env, workdir, user
   - rootfs: merged | overlay | disk strategy
   - mounts: bind mounts into container
   → Returns ContainerInitResponse

6. Execution.Exec(ExecRequest)
   - program, args, env, workdir, timeout_ms, tty
   → Returns ExecResponse with execution_id

7. Execution.Attach(AttachRequest { execution_id })
   → Server-streams ExecOutput (stdout/stderr chunks)

8. Execution.SendInput(stream ExecStdin)  [optional]
   → Client-streams stdin data

9. Execution.Wait(WaitRequest { execution_id })
   → Returns WaitResponse with exit_code

10. Guest.Shutdown(ShutdownRequest) [cleanup]
```

---

## 5. SDK Client Code — Python API Surface

### Core Rust Bindings (exposed via PyO3)

```python
# Runtime
class Boxlite:
    @staticmethod
    def default() -> Boxlite              # Global singleton
    def __init__(options: Options)
    async def create(options: BoxOptions, name: str = None) -> Box
    async def get(id_or_name: str) -> Box | None
    async def get_or_create(options: BoxOptions, name: str = None) -> tuple[Box, bool]
    async def list_info(state: str = None) -> list[BoxInfo]
    async def get_info(id_or_name: str) -> BoxInfo | None
    async def metrics() -> RuntimeMetrics
    async def remove(id_or_name: str, force: bool = False)
    async def shutdown(timeout: int = None)

# Box handle
class Box:
    @property id: str
    @property name: str | None
    def info() -> BoxInfo
    async def exec(command: str, args: list[str], env: list[tuple], tty: bool = False) -> Execution
    async def start()
    async def stop()
    async def metrics() -> BoxMetrics
    async def copy_in(host_path: str, container_dest: str, copy_options: CopyOptions = None)
    async def copy_out(container_src: str, host_dest: str, copy_options: CopyOptions = None)

# Execution handle
class Execution:
    def stdin() -> ExecStdin               # take once
    def stdout() -> ExecStdout             # take once, async iterator
    def stderr() -> ExecStderr             # take once, async iterator
    async def wait() -> ExecResult
    async def kill(signal: int = 9)
    async def resize_tty(rows, cols, x_pixels, y_pixels)

# Options
class Options:
    home_dir: str = None                   # default: ~/.boxlite
    image_registries: list[str] = []

class BoxOptions:
    image: str                             # OCI image reference
    rootfs_path: str = None                # local OCI layout path (overrides image)
    cpus: int = None
    memory_mib: int = None
    disk_size_gb: int = None
    working_dir: str = None
    env: list[tuple[str, str]] = []
    volumes: list = []
    ports: list = []
    auto_remove: bool = True
    detach: bool = False
    entrypoint: list[str] = None
    cmd: list[str] = None
    user: str = None
    security: SecurityOptions = None

class RootfsSpec:
    Image(str)                             # Pull from registry
    RootfsPath(str)                        # Local path

class CopyOptions:
    recursive: bool = True
    overwrite: bool = True
    follow_symlinks: bool = False
    include_parent: bool = True
```

### Python Convenience Wrappers (High-Level API)

#### `SimpleBox` — Base for all specialized boxes

```python
class SimpleBox:
    def __init__(
        image: str = None,
        rootfs_path: str = None,
        memory_mib: int = None,
        cpus: int = None,
        runtime: Boxlite = None,
        name: str = None,
        auto_remove: bool = True,
        reuse_existing: bool = False,
        **kwargs                           # Additional BoxOptions fields
    )

    async def start() -> SimpleBox
    async def exec(cmd: str, *args, env: dict = None) -> ExecResult
    def shutdown()
    async def copy_in(host_path, container_dest, *, overwrite=True, follow_symlinks=False, include_parent=True)
    async def copy_out(container_src, host_dest, *, overwrite=True, follow_symlinks=False, include_parent=True)

    @property id: str
    @property created: bool | None
    def info() -> BoxInfo

    # Async context manager
    async with SimpleBox(image="alpine:latest") as box: ...
```

#### `CodeBox` — Python code execution

```python
class CodeBox(SimpleBox):
    def __init__(image="python:slim", memory_mib=None, cpus=None, runtime=None, **kwargs)
    async def run(code: str, timeout: int = None) -> str    # stdout + stderr
    async def run_script(script_path: str) -> str
    async def install_package(package: str) -> str
    async def install_packages(*packages: str) -> str
```

#### `InteractiveBox` — PTY terminal sessions

```python
class InteractiveBox(SimpleBox):
    def __init__(
        image: str,
        shell: str = "/bin/sh",
        tty: bool = None,                  # None=auto-detect, True=force, False=disable
        memory_mib=None, cpus=None, runtime=None, name=None, auto_remove=True, **kwargs
    )
    async def wait()
    # Auto-starts shell with PTY, forwards stdin/stdout bidirectionally
```

#### `BrowserBox` — Playwright/Puppeteer browser

```python
class BrowserBoxOptions:
    browser: str = "chromium"              # chromium | firefox | webkit
    memory: int = 2048
    cpu: int = 2
    port: int = None                       # Host Playwright Server port (default: 3000)
    cdp_port: int = None                   # Host CDP port (default: 9222)

class BrowserBox(SimpleBox):
    _DEFAULT_IMAGE = "mcr.microsoft.com/playwright:v1.58.0-jammy"
    _DEFAULT_PORT = 3000

    def __init__(options: BrowserBoxOptions = None, runtime=None, **kwargs)
    async def playwright_endpoint(timeout: int = 60) -> str   # ws:// URL
    async def endpoint(timeout: int = 60) -> str              # CDP/BiDi direct URL
```

#### `ComputerBox` — Desktop environment (noVNC)

```python
class ComputerBox(SimpleBox):
    COMPUTERBOX_IMAGE = "lscr.io/linuxserver/webtop:ubuntu-xfce"

    def __init__(cpu=2, memory=2048, gui_http_port=3000, gui_https_port=3001, runtime=None, **kwargs)
    async def wait_until_ready(timeout: int = 60)
    async def screenshot() -> dict                   # {data: base64, width, height, format}
    async def mouse_move(x: int, y: int)
    async def left_click() / right_click() / middle_click() / double_click() / triple_click()
    async def left_click_drag(start_x, start_y, end_x, end_y)
    async def cursor_position() -> tuple[int, int]
    async def type(text: str)
    async def key(text: str)                         # e.g., "Return", "ctrl+c"
    async def scroll(x, y, direction, amount=3)      # up|down|left|right
    async def get_screen_size() -> tuple[int, int]
```

#### `SkillBox` — Claude Code CLI execution

```python
class SkillBox(SimpleBox):
    SKILLBOX_IMAGE = "ghcr.io/boxlite-ai/boxlite-skillbox:0.1.0"

    def __init__(
        skills: list[str] = None,          # e.g., ["anthropics/skills"]
        oauth_token: str = None,           # or CLAUDE_CODE_OAUTH_TOKEN env
        name="skill-box",
        image=SKILLBOX_IMAGE,
        memory_mib=4096,
        disk_size_gb=10,
        gui_http_port=0,                   # 0 = random
        gui_https_port=0,
        auto_remove=True,
        runtime=None, **kwargs
    )
    async def wait_until_ready(timeout: int = 60)
    async def call(prompt: str) -> str
```

#### `ExecResult` — Command output

```python
@dataclass
class ExecResult:
    exit_code: int
    stdout: str
    stderr: str
    error_message: str | None = None       # diagnostic for unexpected death
```

#### Error Hierarchy

```python
class BoxliteError(Exception): ...
class ExecError(BoxliteError):             # command failed
    command: str; exit_code: int; stderr: str
class TimeoutError(BoxliteError): ...
class ParseError(BoxliteError): ...
```

---

## 6. CLI Commands (`boxlite-cli`)

All commands available via the `boxlite` binary:

| Command | Description | Key Flags |
|---------|-------------|-----------|
| `boxlite run` | Create, start, and exec in one step | `--cpus`, `--memory`, `-e/--env`, `-v/--volume`, `-p/--publish`, `-i`, `-t`, `--name`, `--detach`, `--rm`, `--workdir`, `--entrypoint`, `--user`, `--config`, `--registry` |
| `boxlite create` | Create a box (configured, not started) | Same resource/config flags |
| `boxlite start` | Start a stopped box | Box ID or name |
| `boxlite stop` | Stop running box(es) | Box ID or name |
| `boxlite restart` | Restart box(es) | Box ID or name |
| `boxlite exec` | Execute command in running box | `-i`, `-t`, `-e`, `-w` |
| `boxlite ls` / `list` / `ps` | List boxes | State filter |
| `boxlite rm` | Remove box(es) | `--force` |
| `boxlite pull` | Pull OCI image | Image reference |
| `boxlite images` | List cached images | — |
| `boxlite inspect` | Detailed box info | Box ID or name |
| `boxlite cp` | Copy files host↔box | `SRC DEST` |
| `boxlite info` | System-wide runtime info | — |
| `boxlite completion` | Shell completion script | `bash`/`zsh`/`fish` |

### Global Flags

| Flag | Env Var | Description |
|------|---------|-------------|
| `--debug` | — | Enable debug output |
| `--home <PATH>` | `BOXLITE_HOME` | BoxLite home directory (default: `~/.boxlite`) |
| `--registry <REGISTRY>` | — | Image registry (repeatable) |
| `--config <PATH>` | — | JSON config file path |

### Port Publishing Format

```
-p [hostPort:]boxPort[/tcp|udp]
# Examples:
-p 8080:80          # host 8080 → guest 80 (TCP)
-p 3000             # host 3000 → guest 3000 (TCP)
-p 9222:9222/tcp    # explicit TCP
```

### Volume Mount Format

```
-v hostPath:guestPath[:ro]
# Examples:
-v /data:/mnt/data
-v /config:/etc/app:ro
```

---

## 7. Configuration

### Runtime Configuration (`BoxliteOptions`)

JSON file loaded with `--config`:

```json
{
  "home_dir": "/custom/boxlite",
  "image_registries": ["ghcr.io/myorg", "docker.io"]
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `home_dir` | `string` | `~/.boxlite` | Runtime data directory |
| `image_registries` | `string[]` | `[]` (uses docker.io) | Registries for unqualified image refs |

### Box Configuration (`BoxOptions`)

```json
{
  "cpus": 2,
  "memory_mib": 2048,
  "disk_size_gb": 10,
  "working_dir": "/app",
  "env": [["KEY", "VALUE"]],
  "rootfs": {"Image": "alpine:latest"},
  "volumes": [{"host_path": "/data", "guest_path": "/mnt", "read_only": false}],
  "network": "Isolated",
  "ports": [{"host_port": 8080, "guest_port": 80, "protocol": "Tcp"}],
  "auto_remove": true,
  "detach": false,
  "entrypoint": ["/bin/sh"],
  "cmd": ["-c", "echo hello"],
  "user": "1000:1000",
  "security": { ... }
}
```

### Security Configuration (`SecurityOptions`)

Three presets: `development()`, `standard()`, `maximum()`.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `jailer_enabled` | `bool` | `false` | OS-level process isolation |
| `seccomp_enabled` | `bool` | `false` | Syscall filtering (Linux) |
| `uid` | `u32?` | `None` (auto) | UID to drop to |
| `gid` | `u32?` | `None` (auto) | GID to drop to |
| `new_pid_ns` | `bool` | `false` | PID namespace (Linux) |
| `new_net_ns` | `bool` | `false` | Network namespace (Linux) |
| `chroot_base` | `PathBuf` | `/srv/boxlite` | Chroot jail base |
| `chroot_enabled` | `bool` | `true` (Linux) | Filesystem isolation |
| `close_fds` | `bool` | `true` | Close inherited FDs |
| `sanitize_env` | `bool` | `true` | Clear env except allowlist |
| `network_enabled` | `bool` | `true` | Sandbox network access (macOS) |
| `resource_limits.max_open_files` | `u64?` | `None` | RLIMIT_NOFILE |
| `resource_limits.max_file_size` | `u64?` | `None` | RLIMIT_FSIZE |
| `resource_limits.max_processes` | `u64?` | `None` | RLIMIT_NPROC |
| `resource_limits.max_memory` | `u64?` | `None` | RLIMIT_AS |
| `resource_limits.max_cpu_time` | `u64?` | `None` | RLIMIT_CPU |

---

## 8. OCI / Container Image Requirements

### Image Resolution Flow

```
Unqualified ref (e.g., "alpine")
  → Try each registry in image_registries[] order
  → If empty, default to docker.io
  → Pull manifest + layers
  → Cache in ~/.boxlite/images/blobs/ (by digest)
  → Layer deduplication (shared across images)
```

### Rootfs Strategies

| Strategy | Proto | Description |
|----------|-------|-------------|
| **Image** | `RootfsSpec::Image(ref)` | Pull OCI image, extract layers |
| **RootfsPath** | `RootfsSpec::RootfsPath(path)` | Local OCI layout directory |
| **Merged** | `MergedRootfs` | Single merged rootfs (virtiofs) |
| **Overlay** | `OverlayRootfs` | Overlayfs from multiple layers |
| **DiskImage** | `DiskRootfs` | Block device as rootfs (QCOW2 COW) |

### Filesystem Layout

```
~/.boxlite/
├── boxes/                   # Per-box runtime data
│   └── {box-id}/
│       ├── mounts/          # Host-prepared shared filesystem
│       │   └── containers/{cid}/
│       │       ├── overlayfs/{upper,work,diff}
│       │       ├── rootfs/
│       │       ├── layers/
│       │       └── volumes/{name}/
│       ├── rootfs/
│       └── config.json
├── images/                  # OCI image cache
│   ├── blobs/               # Layer blobs by digest
│   └── index.json
├── init/                    # Shared init rootfs
│   └── rootfs/
├── logs/
│   └── boxlite.log          # Daily rotating
├── db/                      # SQLite databases
└── boxlite.lock             # Runtime lock file
```

### Guest-Side Layout

```
/run/boxlite/                # GUEST_BASE
├── shared/                  # virtiofs mount (from host mounts/)
│   └── containers/{cid}/
│       ├── overlayfs/{upper,work,diff}
│       ├── rootfs/
│       └── volumes/{name}/
```

### Virtiofs Mount Tags

| Tag | Description |
|-----|-------------|
| `BoxLiteContainer0Rootfs` | Prepared rootfs (merged mode) |
| `BoxLiteContainer0Layers` | Image layers directory |
| `BoxLiteShared` | Shared container directory |

---

## 9. Network / Port Forwarding

### Network Backend: gvproxy (Default)

```
Box (Guest VM)           gvproxy                    Internet
┌──────────┐          ┌──────────┐              ┌──────────┐
│ eth0     │◄──vsock──│ NAT     │◄──TCP/UDP───▶│ External │
│ 192.168. │          │ DHCP    │              │ Services │
│ 127.2    │          │ DNS     │              └──────────┘
└──────────┘          └──────────┘
                       192.168.127.1
```

### Port Forwarding

Port specs in `BoxOptions.ports`:

```rust
struct PortSpec {
    host_port: Option<u16>,    // None/0 = dynamic
    guest_port: u16,
    protocol: PortProtocol,    // Tcp | Udp
    host_ip: Option<String>,   // default: 0.0.0.0
}
```

Python SDK:
```python
SimpleBox(image="...", ports=[(host_port, guest_port), ...])
```

Node.js SDK:
```typescript
new SimpleBox({ ports: [{ hostPort: 8080, guestPort: 80, protocol: "tcp" }] })
```

### Volume Mounts

```rust
struct VolumeSpec {
    host_path: String,
    guest_path: String,
    read_only: bool,
}
```

Implemented via **virtiofs** (shared filesystem between host and guest VM).

---

## 10. REST/HTTP API Endpoints

**BoxLite does NOT expose any REST/HTTP API.** The runtime is an **embedded library**, not a daemon.

All communication paths:
1. **Host app → Rust core**: Direct function calls (native library binding)
2. **Rust core → Guest VM**: gRPC over Unix socket (bridged to vsock by libkrun)
3. **SDKs (Python/Node.js)**: Native bindings (PyO3/napi-rs) calling Rust core directly

The only HTTP endpoints are **inside guest containers** (e.g., BrowserBox exposes Playwright Server on port 3000 inside the VM, which is port-forwarded to the host).

---

## 11. Node.js SDK API Surface

```typescript
// Core
export class SimpleBox {
  constructor(options: SimpleBoxOptions)
  async exec(cmd: string, ...args: string[]): Promise<ExecResult>
  async stop(): Promise<void>
  get id(): string
}

interface SimpleBoxOptions {
  image?: string
  rootfsPath?: string
  memoryMib?: number
  cpus?: number
  diskSizeGb?: number
  name?: string
  autoRemove?: boolean
  reuseExisting?: boolean
  detach?: boolean
  workingDir?: string
  env?: Record<string, string>
  volumes?: Array<{ hostPath: string; guestPath: string; readOnly?: boolean }>
  ports?: Array<{ hostPort?: number; guestPort: number; protocol?: string }>
  entrypoint?: string[]
  cmd?: string[]
  user?: string
  security?: SecurityOptions
  runtime?: Boxlite
}

// Specialized
export class CodeBox extends SimpleBox { ... }
export class BrowserBox extends SimpleBox { ... }
export class ComputerBox extends SimpleBox { ... }
export class InteractiveBox extends SimpleBox { ... }

interface ExecResult { exitCode: number; stdout: string; stderr: string }

// Errors
export class BoxliteError extends Error { ... }
export class ExecError extends BoxliteError { command; exitCode; stderr }
export class TimeoutError extends BoxliteError { ... }
export class ParseError extends BoxliteError { ... }
```

---

## 12. Guest Agent Implementation

**Location**: `guest/src/`

The guest agent is a **Rust binary** (`boxlite-guest`) compiled for Linux only.

### CLI Arguments

```
boxlite-guest --listen <URI> [--notify <URI>]

# Examples:
boxlite-guest --listen vsock://2695 --notify vsock://2696
boxlite-guest --listen unix:///var/run/boxlite.sock
boxlite-guest --listen tcp://127.0.0.1:8080
```

### Boot Sequence

1. Mount essential tmpfs directories
2. Parse CLI args (`--listen`, `--notify`)
3. Prepare guest layout at `/run/boxlite/`
4. Start gRPC server (uninitialized)
5. Wait for `Guest.Init` RPC (sets up mounts, network)
6. Wait for `Container.Init` RPC (sets up OCI container via libcontainer)
7. Server `Execution.Exec` RPCs (runs commands in container)

### Guest Modules

| Module | Description |
|--------|-------------|
| `service/server.rs` | gRPC server setup and lifecycle |
| `service/guest.rs` | Guest service implementation |
| `service/container.rs` | Container lifecycle (libcontainer) |
| `service/exec/` | Command execution engine |
| `service/files.rs` | File upload/download (tar) |
| `container/` | OCI container management |
| `storage/` | Filesystem mounts, overlayfs |
| `network.rs` | Virtual NIC, DHCP configuration |
| `mounts.rs` | Essential tmpfs mounts |
| `overlayfs.rs` | Overlay filesystem setup |
| `layout.rs` | Guest directory layout |

---

## 13. Summary — What a .NET Client Needs

### Option A: Embed BoxLite (Native Interop)

Like the Python/Node.js SDKs, call the Rust core directly via P/Invoke or a C FFI wrapper (the C SDK exists at `sdks/c/`). This gives access to:
- `BoxliteRuntime` — create/list/remove boxes
- `LiteBox` — exec/start/stop/copy
- Full image management, caching, VM lifecycle

### Option B: gRPC Client to Existing VM

If a BoxLite VM is already running (started by the CLI or another SDK), connect directly to its gRPC services:

1. **Connect** to the Unix socket at `/tmp/boxlite-{id}.sock` (or wherever the transport is)
2. **Use generated gRPC clients** from `boxlite-shared/proto/boxlite/v1/service.proto`:
   - `GuestClient` — `Init()`, `Ping()`, `Shutdown()`
   - `ContainerClient` — `Init()`
   - `ExecutionClient` — `Exec()`, `Attach()`, `SendInput()`, `Wait()`, `Kill()`, `ResizeTty()`
   - `FilesClient` — `Upload()`, `Download()`

### Key Integration Points

| Concern | Solution |
|---------|----------|
| **Proto generation** | Use `Grpc.Tools` NuGet to generate C# from `service.proto` |
| **Transport** | Connect to Unix socket (for host-local VMs) |
| **Exec pattern** | `Exec()` → `Attach()` + `Wait()` (parallel) |
| **Stdin** | `SendInput()` client streaming |
| **File transfer** | tar-based `Upload()`/`Download()` |
| **Lifecycle** | `Guest.Init()` → `Container.Init()` → `Execution.Exec()` → `Guest.Shutdown()` |

### Error Types to Map

| Rust Error | Suggested C# |
|------------|-------------|
| `BoxliteError::NotFound` | `BoxNotFoundException` |
| `BoxliteError::InvalidState` | `InvalidOperationException` |
| `BoxliteError::Portal` / `Rpc` / `RpcTransport` | `GrpcException` |
| `BoxliteError::Execution` | `ExecutionException` |
| `BoxliteError::Image` | `ImageException` |
| `BoxliteError::Config` | `ConfigurationException` |
| `BoxliteError::Stopped` | `ObjectDisposedException` |
