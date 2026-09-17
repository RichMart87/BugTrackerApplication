import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// The dev server proxies /api to the .NET host so the browser sees a single
// origin - no CORS preflight in development and no base URL baked into the build.
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET || 'http://localhost:5174',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: 'dist',
    // build.ps1 copies dist into the API's public wwwroot; maps would publish the
    // original source alongside the bundle.
    sourcemap: false
  }
})
