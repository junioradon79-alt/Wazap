import { useState, useCallback, useRef } from 'react'

/**
 * Hook pour la copie dans le presse-papier avec retour visuel temporisé.
 * Supporte le navigateur moderne (navigator.clipboard) avec repli sécurisé.
 */
export function useClipboard(timeout = 2200) {
  const [copiedKey, setCopiedKey] = useState<string | null>(null)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const copy = useCallback(
    async (text: string, key = 'default'): Promise<boolean> => {
      let success = false
      try {
        if (navigator.clipboard && window.isSecureContext) {
          await navigator.clipboard.writeText(text)
          success = true
        } else {
          // Repli pour contextes non sécurisés ou anciens navigateurs
          const textArea = document.createElement('textarea')
          textArea.value = text
          textArea.style.position = 'fixed'
          textArea.style.left = '-999999px'
          textArea.style.top = '-999999px'
          document.body.appendChild(textArea)
          textArea.focus()
          textArea.select()
          success = document.execCommand('copy')
          document.body.removeChild(textArea)
        }
      } catch {
        success = false
      }

      if (success) {
        setCopiedKey(key)
        if (timerRef.current) clearTimeout(timerRef.current)
        timerRef.current = setTimeout(() => {
          setCopiedKey(null)
        }, timeout)
      }

      return success
    },
    [timeout]
  )

  const isCopied = useCallback((key = 'default') => copiedKey === key, [copiedKey])

  return { copy, isCopied, copiedKey }
}
