import type { ReactNode } from 'react'

export interface FilterTabOption<T extends string = string> {
  id: T
  label: string
  count?: number
  icon?: ReactNode
}

export interface FilterTabsProps<T extends string = string> {
  options: FilterTabOption<T>[]
  value: T
  onChange: (value: T) => void
  className?: string
  ariaLabel?: string
}

export function FilterTabs<T extends string = string>({
  options,
  value,
  onChange,
  className = '',
  ariaLabel = 'Filtrer les éléments',
}: FilterTabsProps<T>) {
  return (
    <div className={`filter-tabs ${className}`.trim()} role="tablist" aria-label={ariaLabel}>
      {options.map((option) => {
        const isActive = option.id === value
        return (
          <button
            key={option.id}
            type="button"
            role="tab"
            aria-selected={isActive}
            className={`filter-tabs__item ${isActive ? 'filter-tabs__item--active' : ''}`.trim()}
            onClick={() => onChange(option.id)}
          >
            {option.icon && <span className="filter-tabs__icon">{option.icon}</span>}
            <span className="filter-tabs__label">{option.label}</span>
            {typeof option.count === 'number' && (
              <span className={`filter-tabs__badge ${isActive ? 'filter-tabs__badge--active' : ''}`}>
                {option.count}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
