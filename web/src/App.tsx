import { Navigate, Route, Routes } from 'react-router-dom'
import type { ReactNode } from 'react'
import Layout from './components/Layout'
import { useAuth } from './auth/AuthContext'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import PacksPage from './pages/PacksPage'
import TransactionsPage from './pages/TransactionsPage'
import VendorsPage from './pages/VendorsPage'
import RidersPage from './pages/RidersPage'
import OrdersPage from './pages/OrdersPage'
import AccountPage from './pages/AccountPage'
import SuiviPage from './pages/SuiviPage'

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
      {/* Page publique de suivi acheteur (aucune authentification) */}
      <Route path="/suivi/:id" element={<SuiviPage />} />
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
        <Route path="/" element={<DashboardPage />} />
        <Route path="/packs" element={<PacksPage />} />
        <Route path="/transactions" element={<TransactionsPage />} />
        <Route path="/vendors" element={<VendorsPage />} />
        <Route path="/riders" element={<RidersPage />} />
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/account" element={<AccountPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
