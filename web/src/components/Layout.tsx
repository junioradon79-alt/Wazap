import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

const NAV = [
  { to: '/', label: 'Tableau de bord', icon: '📊', end: true },
  { to: '/orders', label: 'Commandes', icon: '🧾' },
  { to: '/riders', label: 'Livreurs', icon: '🛵' },
  { to: '/vendors', label: 'Vendeurs', icon: '🏪' },
  { to: '/packs', label: 'Packs', icon: '💳' },
  { to: '/transactions', label: 'Transactions', icon: '📒' },
  { to: '/account', label: 'Mon compte', icon: '👤' },
]

export default function Layout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = (): void => {
    logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar__brand">
          <img src={`${import.meta.env.BASE_URL}logo.png`} alt="WAZAP" className="brand__logo-img" />
        </div>

        <nav className="sidebar__nav">
          <span className="nav__section">Pilotage</span>
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `nav__item${isActive ? ' active' : ''}`}
            >
              <span className="nav__icon">{item.icon}</span> {item.label}
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
          <button className="btn" onClick={handleLogout} style={{ width: '100%', marginTop: 10 }}>
            Déconnexion
          </button>
        </div>
      </aside>

      <main className="main">
        <header className="topbar">
          <div>
            <h1 className="topbar__title">Administration WAZAP</h1>
            <p className="topbar__subtitle">Plateforme de livraison &amp; packs prépayés</p>
          </div>
          <div className="topbar__actions">
            <span className="topbar__live"><span className="topbar__dot" /> Live</span>
          </div>
        </header>

        <div className="main__content">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
