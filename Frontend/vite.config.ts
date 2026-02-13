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
      "/api": {
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
