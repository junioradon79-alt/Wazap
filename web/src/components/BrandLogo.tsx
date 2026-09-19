interface BrandLogoProps {
  size?: 'sm' | 'md' | 'lg' | 'xl'
  variant?: 'badge' | 'inline'
  className?: string
  showTagline?: boolean
}

const SIZES = {
  sm: { img: 32, text: 16, sub: 10 },
  md: { img: 44, text: 20, sub: 11 },
  lg: { img: 64, text: 26, sub: 13 },
  xl: { img: 96, text: 34, sub: 15 },
}

export default function BrandLogo({
  size = 'md',
  variant = 'inline',
  className = '',
  showTagline = true,
}: BrandLogoProps) {
  const dim = SIZES[size]
  const base = import.meta.env.BASE_URL || '/app/'
  const badgeUrl = `${base}logo-badge.png`

  if (variant === 'badge') {
    return (
      <div
        className={`brand-logo-badge-wrap ${className}`}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <img
          src={badgeUrl}
          alt="WAZAP — Logistique par WhatsApp"
          style={{
            width: dim.img * 1.6,
            height: dim.img * 1.6,
            borderRadius: '50%',
            objectFit: 'contain',
            filter: 'drop-shadow(0 4px 14px rgba(0, 214, 108, 0.25))',
          }}
        />
      </div>
    )
  }

  return (
    <div
      className={`brand-logo-inline ${className}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: size === 'sm' ? 8 : 12,
        textDecoration: 'none',
      }}
    >
      <img
        src={badgeUrl}
        alt="WAZAP Logo"
        style={{
          width: dim.img,
          height: dim.img,
          borderRadius: '50%',
          objectFit: 'contain',
          flexShrink: 0,
          background: 'rgba(255, 255, 255, 0.05)',
          padding: 2,
          boxShadow: '0 2px 10px rgba(0, 214, 108, 0.2)',
        }}
      />
      <div style={{ display: 'flex', flexDirection: 'column', lineHeight: 1.1 }}>
        <span
          style={{
            fontWeight: 900,
            fontSize: dim.text,
            letterSpacing: '-0.5px',
            color: '#fff',
            fontFamily: 'system-ui, -apple-system, sans-serif',
          }}
        >
          WAZAP
        </span>
        {showTagline && (
          <span
            style={{
              fontSize: dim.sub,
              color: 'var(--suivi-emerald, #00d66c)',
              fontWeight: 600,
              letterSpacing: '0.2px',
            }}
          >
            Logistique par WhatsApp
          </span>
        )}
      </div>
    </div>
  )
}
