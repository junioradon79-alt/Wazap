import { useMemo } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import type { UserRole } from '../api/types'
import BrandLogo from './BrandLogo'

interface NavItem {
  to: string
  label: string
  icon: string
  end?: boolean
  roles: UserRole[]
}

const ALL_NAV_ITEMS: NavItem[] = [
  { to: '/', label: 'Tableau de bord', icon: '📊', end: true, roles: ['Admin', 'Vendor', 'Rider'] },
  { to: '/orders', label: 'Commandes', icon: '🧾', roles: ['Admin', 'Vendor'] },
  { to: '/catalogue', label: 'Catalogue', icon: '🛒', roles: ['Admin', 'Vendor'] },
  { to: '/riders', label: 'Livreurs', icon: '🛵', roles: ['Admin'] },
  { to: '/vendors', label: 'Vendeurs', icon: '🏪', roles: ['Admin'] },
  { to: '/leads', label: 'Leads', icon: '🎯', roles: ['Admin'] },
  { to: '/claims', label: 'Sinistres', icon: '🚨', roles: ['Admin'] },
  { to: '/avis', label: 'Avis', icon: '⭐', roles: ['Admin'] },
  { to: '/whatsapp', label: 'WhatsApp & Coûts', icon: '💬', roles: ['Admin'] },
  { to: '/packs', label: 'Packs', icon: '💳', roles: ['Admin', 'Vendor'] },
  { to: '/transactions', label: 'Transactions', icon: '📒', roles: ['Admin'] },
  { to: '/account', label: 'Mon compte', icon: '👤', roles: ['Admin', 'Vendor', 'Rider'] },
]

export default function Layout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = (): void => {
    logout()
    navigate('/login', { replace: true })
  }

  const role = user?.role as UserRole | undefined

  const navItems = useMemo(() => {
    if (!role) return []
    return ALL_NAV_ITEMS.filter((item) => item.roles.includes(role))
  }, [role])

  const sectionTitle = useMemo(() => {
    if (role === 'Vendor') return 'Mon activité'
    if (role === 'Rider') return 'Espace Livreur'
    return 'Pilotage WAZAP'
  }, [role])

  const topbarTitle = useMemo(() => {
    if (role === 'Rider') return 'Espace Livreur WAZAP'
    if (role === 'Vendor') return 'Espace Marchand WAZAP'
    return 'Administration WAZAP'
  }, [role])

  const topbarSubtitle = useMemo(() => {
    if (role === 'Rider') return 'Cockpit coursier · 0% commission · Grand Abidjan'
    if (role === 'Vendor') return 'Gestion des expéditions & packs prépayés'
    return 'Plateforme de livraison & packs prépayés'
  }, [role])

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar__brand" style={{ display: 'flex', justifyContent: 'center', padding: '12px 10px 20px' }}>
          <BrandLogo size="md" variant="inline" />
        </div>

        <nav className="sidebar__nav" aria-label="Navigation principale">
          <span className="nav__section">{sectionTitle}</span>
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `nav__item${isActive ? ' active' : ''}`}
            >
              <span className="nav__icon" aria-hidden="true">{item.icon}</span> {item.label}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar__footer">
          <div className="user">
            <div className="user__avatar">{(user?.username?.[0] ?? '?').toUpperCase()}</div>
            <div className="user__meta">
              <span className="user__name">{user?.username ?? '—'}</span>
              <span className="user__role">{user?.role ?? '—'}</span>
            </div>
          </div>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={handleLogout}
            style={{ width: '100%', marginTop: 10, borderColor: 'rgba(255, 255, 255, 0.15)', color: '#cbd5e1' }}
          >
            Déconnexion
          </button>
        </div>
      </aside>

      <main className="main">
        <header className="topbar">
          <div>
            <h1 className="topbar__title">{topbarTitle}</h1>
            <p className="topbar__subtitle">{topbarSubtitle}</p>
          </div>
          <div className="topbar__actions">
            <span className="topbar__live">
              <span className="topbar__dot" /> Live
            </span>
          </div>
        </header>

        <div className="main__content">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
