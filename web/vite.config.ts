import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// base '/app/' : en production le build est servi depuis /app (wwwroot/app de l'API).
export default defineConfig({
  plugins: [react()],
  base: '/app/',
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5297',
      '/health': 'http://localhost:5297',
    },
  },
  build: {
    // Conserver la compatibilité mobile du build précédent lors du passage à Vite 8.
    target: ['chrome87', 'edge88', 'firefox78', 'safari14'],
    outDir: 'dist',
    emptyOutDir: true,
  },
})
