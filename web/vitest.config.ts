import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Configuration des tests front (Vitest + jsdom + Testing Library).
// Le projet n'avait AUCUN test front alors qu'il porte l'authentification, l'argent
// (paiements, crédits) et 16 pages : les régressions n'étaient détectées qu'en production.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
    restoreMocks: true,
  },
})
