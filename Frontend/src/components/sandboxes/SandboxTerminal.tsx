import { useCallback, useEffect, useRef, useState } from "react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";

type ConnectionStatus = "connecting" | "connected" | "disconnected" | "error";

interface SandboxTerminalProps {
  sandboxId: string;
  /** 是否自动连接，默认 true */
  autoConnect?: boolean;
}

/**
 * 基于 xterm.js 的沙箱 Web Terminal 组件。
 *
 * 协议：
 *   - 文本帧(client→server) = stdin 数据
 *   - 二进制帧(client→server) = 控制消息 [0x01, cols_hi, cols_lo, rows_hi, rows_lo]
 *   - 二进制帧(server→client) = stdout 数据
 */
export function SandboxTerminal({ sandboxId, autoConnect = true }: SandboxTerminalProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const termRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>("disconnected");

  const connect = useCallback(() => {
    if (!containerRef.current) return;

    // ── 清理旧实例 ──
    wsRef.current?.close();
    termRef.current?.dispose();

    // ── 创建 xterm 终端 ──
    const term = new Terminal({
      cursorBlink: true,
      fontSize: 14,
      fontFamily: "'Cascadia Code', 'Fira Code', 'JetBrains Mono', Menlo, Monaco, monospace",
      theme: {
        background: "#1a1b26",
        foreground: "#c0caf5",
        cursor: "#c0caf5",
        selectionBackground: "#33467c",
        black: "#15161e",
        red: "#f7768e",
        green: "#9ece6a",
        yellow: "#e0af68",
        blue: "#7aa2f7",
        magenta: "#bb9af7",
        cyan: "#7dcfff",
        white: "#a9b1d6",
        brightBlack: "#414868",
        brightRed: "#f7768e",
        brightGreen: "#9ece6a",
        brightYellow: "#e0af68",
        brightBlue: "#7aa2f7",
        brightMagenta: "#bb9af7",
        brightCyan: "#7dcfff",
        brightWhite: "#c0caf5",
      },
    });

    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);
    term.open(containerRef.current);
    fitAddon.fit();

    termRef.current = term;
    fitAddonRef.current = fitAddon;

    // ── 构建 WebSocket URL ──
    const proto = window.location.protocol === "https:" ? "wss:" : "ws:";
    const wsUrl = `${proto}//${window.location.host}/api/sandboxes/${sandboxId}/terminal`;

    setStatus("connecting");
    term.write("\x1b[33m⏳ 正在连接终端...\x1b[0m\r\n");

    const ws = new WebSocket(wsUrl);
    ws.binaryType = "arraybuffer";
    wsRef.current = ws;

    ws.onopen = () => {
      setStatus("connected");
      term.write("\x1b[32m✔ 终端已连接\x1b[0m\r\n\r\n");

      // 发送初始 resize
      sendResize(ws, term.cols, term.rows);
    };

    ws.onmessage = (ev) => {
      if (ev.data instanceof ArrayBuffer) {
        term.write(new Uint8Array(ev.data));
      } else if (typeof ev.data === "string") {
        term.write(ev.data);
      }
    };

    ws.onerror = () => {
      setStatus("error");
      term.write("\r\n\x1b[31m✘ 连接错误\x1b[0m\r\n");
    };

    ws.onclose = (ev) => {
      setStatus("disconnected");
      term.write(`\r\n\x1b[33m⏏ 终端已断开 (${ev.code})\x1b[0m\r\n`);
    };

    // ── 终端输入 → WebSocket ──
    term.onData((data) => {
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(data);
      }
    });

    // ── 终端 resize → WebSocket ──
    term.onResize(({ cols, rows }) => {
      if (ws.readyState === WebSocket.OPEN) {
        sendResize(ws, cols, rows);
      }
    });
  }, [sandboxId]);

  // ── 容器尺寸变化时 fit ──
  useEffect(() => {
    if (!containerRef.current) return;

    const observer = new ResizeObserver(() => {
      fitAddonRef.current?.fit();
    });
    observer.observe(containerRef.current);

    return () => observer.disconnect();
  }, []);

  // ── 自动连接 & 清理 ──
  useEffect(() => {
    if (autoConnect) {
      connect();
    }

    return () => {
      wsRef.current?.close();
      termRef.current?.dispose();
    };
  }, [autoConnect, connect]);

  return (
    <div className="flex flex-col h-full">
      {/* 状态栏 */}
      <div className="flex items-center justify-between px-3 py-1.5 bg-[#1a1b26] border-b border-[#33467c] rounded-t-md">
        <div className="flex items-center gap-2 text-xs">
          <span
            className={`inline-block h-2 w-2 rounded-full ${
              status === "connected"
                ? "bg-green-400"
                : status === "connecting"
                  ? "bg-yellow-400 animate-pulse"
                  : status === "error"
                    ? "bg-red-400"
                    : "bg-gray-500"
            }`}
          />
          <span className="text-gray-400">
            {status === "connected"
              ? "已连接"
              : status === "connecting"
                ? "连接中..."
                : status === "error"
                  ? "连接失败"
                  : "已断开"}
          </span>
        </div>

        {status === "disconnected" || status === "error" ? (
          <button
            onClick={connect}
            className="text-xs text-blue-400 hover:text-blue-300 transition-colors"
          >
            重新连接
          </button>
        ) : null}
      </div>

      {/* 终端容器 */}
      <div
        ref={containerRef}
        className="flex-1 min-h-0 bg-[#1a1b26] rounded-b-md p-1"
      />
    </div>
  );
}

/**
 * 发送 resize 控制消息（二进制帧）
 * 格式：[0x01, cols_hi, cols_lo, rows_hi, rows_lo]
 */
function sendResize(ws: WebSocket, cols: number, rows: number) {
  const buf = new Uint8Array(5);
  buf[0] = 0x01;
  buf[1] = (cols >> 8) & 0xff;
  buf[2] = cols & 0xff;
  buf[3] = (rows >> 8) & 0xff;
  buf[4] = rows & 0xff;
  ws.send(buf.buffer);
}
