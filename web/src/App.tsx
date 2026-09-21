import { lazy, Suspense, type ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import { useAuth } from './auth/AuthContext'

// Découpage du bundle : chaque page est chargée à la demande. Sans cela, un visiteur qui
// ouvre la page de vente ou son lien de suivi (mobile, réseau lent) téléchargeait TOUT le
// back-office — un seul fichier de ~280 Ko.
const LoginPage = lazy(() => import('./pages/LoginPage'))
const DashboardPage = lazy(() => import('./pages/DashboardPage'))
const VendorDashboardPage = lazy(() => import('./pages/VendorDashboardPage'))
const PacksPage = lazy(() => import('./pages/PacksPage'))
const TransactionsPage = lazy(() => import('./pages/TransactionsPage'))
const VendorsPage = lazy(() => import('./pages/VendorsPage'))
const CataloguePage = lazy(() => import('./pages/CataloguePage'))
const RidersPage = lazy(() => import('./pages/RidersPage'))
const OrdersPage = lazy(() => import('./pages/OrdersPage'))
const LeadsPage = lazy(() => import('./pages/LeadsPage'))
const ClaimsPage = lazy(() => import('./pages/ClaimsPage'))
const RatingsPage = lazy(() => import('./pages/RatingsPage'))
const AccountPage = lazy(() => import('./pages/AccountPage'))
const WhatsAppLogsPage = lazy(() => import('./pages/WhatsAppLogsPage'))
const SuiviPage = lazy(() => import('./pages/SuiviPage'))
const VentePage = lazy(() => import('./pages/VentePage'))
const ParrainagePage = lazy(() => import('./pages/ParrainagePage'))

function PageLoader() {
  return (
    <div className="loading">
      <span className="loading__spinner" /> Chargement…
    </div>
  )
}

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
  const { user, loading } = useAuth()

  if (loading) return <PageLoader />

  return (
    // Le repli s'affiche le temps de charger le morceau de page demandé.
    <Suspense fallback={<PageLoader />}>
      <Routes>
        {/* Pages publiques : suivi acheteur & parrainage */}
        <Route path="/suivi/:id" element={<SuiviPage />} />
        <Route path="/vente" element={<VentePage />} />
        <Route path="/parrainage" element={<ParrainagePage />} />
        <Route
          path="/login"
          element={user ? <Navigate to="/" replace /> : <LoginPage />}
        />

        {/* Espace connecté (Admin & Marchand) */}
        <Route
          element={
            <Protected>
              <Layout />
            </Protected>
          }
        >
          {user && (
            <Route
              path="/"
              element={user.role === 'Vendor' ? <VendorDashboardPage /> : <DashboardPage />}
            />
          )}
          <Route path="/packs" element={<PacksPage />} />
          <Route path="/transactions" element={<TransactionsPage />} />
          <Route path="/vendors" element={<VendorsPage />} />
          <Route path="/catalogue" element={<CataloguePage />} />
          <Route path="/leads" element={<LeadsPage />} />
          <Route path="/claims" element={<ClaimsPage />} />
          <Route path="/avis" element={<RatingsPage />} />
          <Route path="/riders" element={<RidersPage />} />
          <Route path="/orders" element={<OrdersPage />} />
          <Route
            path="/whatsapp"
            element={user?.role === 'Admin' ? <WhatsAppLogsPage /> : <Navigate to="/" replace />}
          />
          <Route path="/account" element={<AccountPage />} />
        </Route>

        {/* Visiteurs non connectés : la racine / affiche la vitrine officielle VentePage */}
        {!user && <Route path="/" element={<VentePage />} />}

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Suspense>
  )
}
