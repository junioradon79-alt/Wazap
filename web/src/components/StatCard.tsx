import type { ReactNode } from 'react'

export interface StatCardProps {
  label: string
  value: ReactNode
  hint?: ReactNode
  icon?: ReactNode
  variant?: 'green' | 'blue' | 'dark' | 'orange' | 'purple'
  className?: string
  trend?: {
    value: number
    label?: string
  }
}

export function StatCard({
  label,
  value,
  hint,
  icon,
  variant = 'blue',
  className = '',
  trend,
}: StatCardProps) {
  return (
    <article className={`stat-card stat-card--${variant} ${className}`.trim()}>
      {icon && <div className="stat-card__icon" aria-hidden="true">{icon}</div>}
      <div className="stat-card__body">
        <span className="stat-card__label">{label}</span>
        <div className="stat-card__value-row">
          <span className="stat-card__value">{value}</span>
          {trend && (
            <span
              className={`stat-card__trend ${
                trend.value >= 0 ? 'stat-card__trend--up' : 'stat-card__trend--down'
              }`}
            >
              {trend.value >= 0 ? '↑' : '↓'} {Math.abs(trend.value)}%
            </span>
          )}
        </div>
        {hint && <span className="stat-card__hint">{hint}</span>}
      </div>
    </article>
  )
}

export function StatsGrid({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`stats ${className}`.trim()}>{children}</section>
}
