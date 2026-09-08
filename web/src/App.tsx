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
  const { user, loading } = useAuth()
  if (loading) return <div className="loading"><span className="loading__spinner" /> Chargement…</div>
  if (!user) return <Navigate to="/login" replace />
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
