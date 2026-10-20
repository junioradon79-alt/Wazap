import { execFileSync } from 'node:child_process'
import { readFileSync, statSync } from 'node:fs'

// Contrôle sans impression des valeurs. Complète le secret scanning du fournisseur Git.
const files = [...new Set(execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], { encoding: 'utf8' }).split('\0').filter(Boolean))]
const patterns = [
  ['private-key', /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/],
  ['github-token', /\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b/],
  ['google-api-key', /\bAIza[A-Za-z0-9_-]{30,}\b/],
  ['live-secret-key', /\bsk_live_[A-Za-z0-9]{16,}\b/],
  ['inline-password', /(?:storePassword|keyPassword)\s*=\s*["'][^"'\r\n]{6,}["']/],
]
let found = 0
for (const file of files) {
  if (/^(?:tests|docs|marketing|strategie)\//.test(file) || !/\.(?:cs|json|ya?ml|kt|kts|ps1|properties|sh|py|tsx?|mjs|env)$/.test(file)) continue
  try {
    if (statSync(file).size > 2_000_000) continue
    const lines = readFileSync(file, 'utf8').split(/\r?\n/)
    for (const [index, line] of lines.entries()) {
      for (const [rule, pattern] of patterns) {
        if (pattern.test(line)) { console.error(`${file}:${index + 1}: ${rule} (valeur masquée)`); found++ }
      }
    }
  } catch (error) { if (error.code !== 'ENOENT') throw error }
}
if (found) process.exitCode = 1
else console.log('Contrôle des signatures de secrets : aucun résultat dans les sources/configurations examinées.')
