import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'src/FireApp/wwwroot',
    manifest: true,
    rollupOptions: {
      input: 'src/FireApp/Assets/js/main.tsx',
    },
  },
  server: {
    strictPort: true,
  },
})
