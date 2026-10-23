export interface PaginationProps {
  currentPage: number
  totalPages: number
  onPageChange: (page: number) => void
  disabled?: boolean
  className?: string
}

export function Pagination({
  currentPage,
  totalPages,
  onPageChange,
  disabled = false,
  className = '',
}: PaginationProps) {
  if (totalPages <= 1) return null

  return (
    <nav className={`pagination ${className}`.trim()} aria-label="Pagination">
      <button
        type="button"
        className="btn btn--ghost pagination__btn"
        onClick={() => onPageChange(currentPage - 1)}
        disabled={disabled || currentPage <= 1}
        aria-label="Page précédente"
      >
        ← Précédent
      </button>

      <span className="pagination__info">
        Page <strong>{currentPage}</strong> sur <strong>{totalPages}</strong>
      </span>

      <button
        type="button"
        className="btn btn--ghost pagination__btn"
        onClick={() => onPageChange(currentPage + 1)}
        disabled={disabled || currentPage >= totalPages}
        aria-label="Page suivante"
      >
        Suivant →
      </button>
    </nav>
  )
}
