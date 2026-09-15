import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

// Le front n'avait AUCUN lint : ni règles de hooks (dépendances de useEffect, usage conditionnel),
// ni détection de code mort. Configuration « recommandée » TypeScript + React.
export default tseslint.config(
  { ignores: ['dist', 'node_modules'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2022,
      globals: { ...globals.browser, ...globals.es2021 },
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // Désactivé volontairement : cette règle ne concerne que la GRANULARITÉ du rechargement à
      // chaud, pas la justesse du code. Nos modules mêlent légitimement composants et fonctions
      // utilitaires (formatage, badges), et les scinder n'apporterait rien au produit.
      'react-refresh/only-export-components': 'off',
      // Le typage est déjà vérifié par `tsc --noEmit` (script typecheck) : ici on se concentre
      // sur les erreurs de logique et de style de code.
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      'no-console': ['warn', { allow: ['warn', 'error'] }],
      eqeqeq: ['error', 'smart'],
    },
  },
  {
    // Les tests ont le droit d'utiliser les globales de Vitest et de journaliser.
    files: ['**/*.test.{ts,tsx}', 'src/test/**/*.ts'],
    languageOptions: { globals: { ...globals.node } },
    rules: { 'no-console': 'off' },
  },
)
