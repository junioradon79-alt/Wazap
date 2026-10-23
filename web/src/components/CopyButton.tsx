import { useState } from 'react'

export interface CopyButtonProps {
  text: string
  label?: string
  copiedLabel?: string
  className?: string
  title?: string
  size?: 'sm' | 'md'
}

export function CopyButton({
  text,
  label = 'Copier',
  copiedLabel = 'Copié !',
  className = '',
  title = 'Copier dans le presse-papier',
  size = 'md',
}: CopyButtonProps) {
  const [copied, setCopied] = useState(false)

  const handleCopy = async () => {
    try {
      if (navigator.clipboard) {
        await navigator.clipboard.writeText(text)
      } else {
        const textarea = document.createElement('textarea')
        textarea.value = text
        textarea.style.position = 'fixed'
        textarea.style.opacity = '0'
        document.body.appendChild(textarea)
        textarea.select()
        document.execCommand('copy')
        document.body.removeChild(textarea)
      }
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // repli
    }
  }

  return (
    <button
      type="button"
      onClick={handleCopy}
      title={title}
      className={`btn ${copied ? 'btn--copied' : ''} ${size === 'sm' ? 'btn--sm' : ''} ${className}`.trim()}
      aria-label={copied ? copiedLabel : label}
    >
      <span aria-hidden="true">{copied ? '✓' : '📋'}</span>
      <span>{copied ? copiedLabel : label}</span>
    </button>
  )
}
