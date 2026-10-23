import type { CSSProperties } from 'react'

export interface SkeletonProps {
  width?: string | number
  height?: string | number
  borderRadius?: string | number
  className?: string
  style?: CSSProperties
}

export function Skeleton({
  width = '100%',
  height = '1rem',
  borderRadius = '8px',
  className = '',
  style,
}: SkeletonProps) {
  const customStyle: CSSProperties = {
    width,
    height,
    borderRadius,
    ...style,
  }

  return <div className={`skeleton ${className}`.trim()} style={customStyle} aria-hidden="true" />
}

export function SkeletonStatCard() {
  return (
    <article className="stat-card stat-card--dark stat-card--skeleton">
      <Skeleton width={44} height={44} borderRadius={12} />
      <div className="stat-card__body" style={{ width: '100%' }}>
        <Skeleton width="60%" height="0.85rem" style={{ marginBottom: 8 }} />
        <Skeleton width="80%" height="1.75rem" style={{ marginBottom: 8 }} />
        <Skeleton width="40%" height="0.75rem" />
      </div>
    </article>
  )
}
