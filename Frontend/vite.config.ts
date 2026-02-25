import path from "path"
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react-swc'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    proxy: {
      // SSE streaming endpoint — must be BEFORE the generic /api proxy
      // to ensure SSE-specific configuration takes effect
      "/api/chat/stream": {
        target: "http://localhost:5156",
        changeOrigin: true,
        // Disable proxy response buffering for SSE
        selfHandleResponse: false,
        configure: (proxy) => {
          proxy.on('proxyRes', (proxyRes, _req, res) => {
            // Force disable buffering headers
            proxyRes.headers['x-accel-buffering'] = 'no';
            proxyRes.headers['cache-control'] = 'no-cache, no-store';
            // Ensure the proxy pipes data immediately
            if (proxyRes.headers['content-type']?.includes('text/event-stream')) {
              // Disable Node.js response buffering
              (res as any).flushHeaders?.();
            }
          });
        },
      },
      "/api": {
        target: "http://localhost:5156",
        changeOrigin: true,
        ws: true,
      },
      "/hubs": {
        target: "http://localhost:5156",
        changeOrigin: true,
        ws: true,
      },
      "/health": {
        target: "http://localhost:5156",
        changeOrigin: true,
      },
      "/alive": {
        target: "http://localhost:5156",
        changeOrigin: true,
      },
    },
  },
})
