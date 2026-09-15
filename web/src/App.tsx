import { Navigate, Route, Routes } from 'react-router-dom'
import type { ReactNode } from 'react'
import Layout from './components/Layout'
import { useAuth } from './auth/AuthContext'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import VendorDashboardPage from './pages/VendorDashboardPage'
import PacksPage from './pages/PacksPage'
import TransactionsPage from './pages/TransactionsPage'
import VendorsPage from './pages/VendorsPage'
import CataloguePage from './pages/CataloguePage'
import RidersPage from './pages/RidersPage'
import OrdersPage from './pages/OrdersPage'
import LeadsPage from './pages/LeadsPage'
import ClaimsPage from './pages/ClaimsPage'
import RatingsPage from './pages/RatingsPage'
import AccountPage from './pages/AccountPage'
import SuiviPage from './pages/SuiviPage'
import VentePage from './pages/VentePage'
import ParrainagePage from './pages/ParrainagePage'

function Protected({ children }: { children: ReactNode }) {
  const { user, loading, logout } = useAuth()
  if (loading) return <div className="loading"><span className="loading__spinner" /> Chargement…</div>
  if (!user) return <Navigate to="/login" replace />

  // L'espace d'administration n'est ouvert qu'aux rôles Admin et Vendor. Un compte Livreur ou
  // Client qui atteignait /app voyait la coquille d'administration et une succession de 403
  // sans explication : on affiche désormais une porte fermée explicite (le serveur applique
  // déjà ses propres contrôles — c'est une défense en profondeur, pas une autorisation).
  if (user.role !== 'Admin' && user.role !== 'Vendor') {
    return (
      <div className="app-shell" style={{ display: 'block', padding: '2rem' }}>
        <div className="card" style={{ maxWidth: 560, margin: '4rem auto' }}>
          <h1 className="topbar__title">Espace réservé</h1>
          <p className="topbar__subtitle">
            Cet espace est réservé aux vendeurs et aux administrateurs WAZAP. Votre compte
            « {user.role} » s’utilise directement sur WhatsApp.
          </p>
          <button className="btn btn--primary" style={{ marginTop: 12 }} onClick={logout}>
            Se déconnecter
          </button>
        </div>
      </div>
    )
  }

  return <>{children}</>
}

export default function App() {
  const { user } = useAuth()

  return (
    <Routes>
      {/* Pages publiques : suivi acheteur & page de vente (aucune authentification) */}
      <Route path="/suivi/:id" element={<SuiviPage />} />
      <Route path="/vente" element={<VentePage />} />
      <Route path="/parrainage" element={<ParrainagePage />} />
      <Route
        path="/login"
        element={user ? <Navigate to="/" replace /> : <LoginPage />}
      />
      <Route
        element={
          <Protected>
            <Layout />
          </Protected>
        }
      >
        <Route path="/" element={user?.role === 'Vendor' ? <VendorDashboardPage /> : <DashboardPage />} />
        <Route path="/packs" element={<PacksPage />} />
        <Route path="/transactions" element={<TransactionsPage />} />
        <Route path="/vendors" element={<VendorsPage />} />
        <Route path="/catalogue" element={<CataloguePage />} />
        <Route path="/leads" element={<LeadsPage />} />
        <Route path="/claims" element={<ClaimsPage />} />
        <Route path="/avis" element={<RatingsPage />} />
        <Route path="/riders" element={<RidersPage />} />
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/account" element={<AccountPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
