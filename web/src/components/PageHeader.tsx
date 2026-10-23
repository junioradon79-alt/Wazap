import type { ReactNode } from 'react'

export interface PageHeaderProps {
  title: string
  subtitle?: ReactNode
  actions?: ReactNode
  badge?: ReactNode
  className?: string
}

export function PageHeader({ title, subtitle, actions, badge, className = '' }: PageHeaderProps) {
  return (
    <div className={`page-head ${className}`.trim()}>
      <div className="page-head__title-group">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <h1>{title}</h1>
          {badge}
        </div>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions && <div className="page-head__actions">{actions}</div>}
    </div>
  )
}
