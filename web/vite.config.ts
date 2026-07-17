import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/AgentSkillStore.Server/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('/node_modules/antd/') || id.includes('/node_modules/@ant-design/')) return 'antd'
          if (id.includes('/node_modules/react-markdown/') || id.includes('/node_modules/remark-') || id.includes('/node_modules/mdast-')) return 'markdown'
          if (id.includes('/node_modules/react/') || id.includes('/node_modules/react-dom/') || id.includes('/node_modules/react-router')) return 'react'
          return undefined
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:8081',
      '/health': 'http://localhost:8081',
      '/manifest.json': 'http://localhost:8081',
    },
  },
})
