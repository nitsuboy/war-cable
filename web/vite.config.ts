import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Relative base so the build runs from any path, including the itch.io zip
// where index.html sits at the archive root.
export default defineConfig({
  base: './',
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/hub': {
        target: 'http://127.0.0.1:5000',
        ws: true,
      },
    },
    fs: {
      allow: ['..'],
    },
  },
})
