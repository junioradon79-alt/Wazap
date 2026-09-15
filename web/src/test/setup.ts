import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

// Nettoyage du DOM entre les tests (sinon le rendu d'un test fuit dans le suivant).
afterEach(() => {
  cleanup()
  localStorage.clear()
  vi.restoreAllMocks()
})
