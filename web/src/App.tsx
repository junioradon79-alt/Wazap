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
const RiderDashboardPage = lazy(() => import('./pages/RiderDashboardPage'))
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
const LivreurPage = lazy(() => import('./pages/LivreurPage'))
const ParrainagePage = lazy(() => import('./pages/ParrainagePage'))
const BoutiqueClientPage = lazy(() => import('./pages/BoutiqueClientPage'))

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

  // L'espace connecté est ouvert aux rôles Admin, Vendor et Rider.
  if (user.role !== 'Admin' && user.role !== 'Vendor' && user.role !== 'Rider') {
    return (
      <div className="app-shell" style={{ display: 'block', padding: '2rem' }}>
        <div className="card" style={{ maxWidth: 560, margin: '4rem auto' }}>
          <h1 className="topbar__title">Espace réservé</h1>
          <p className="topbar__subtitle">
            Cet espace est réservé aux commerçants, livreurs et administrateurs WAZAP. Votre compte
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

function RolePage({ roles, children }: { roles: string[]; children: ReactNode }) {
  const { user } = useAuth()
  return user && roles.includes(user.role) ? <>{children}</> : <Navigate to="/" replace />
}

export default function App() {
  const { user, loading } = useAuth()

  if (loading) return <PageLoader />

  return (
    // Le repli s'affiche le temps de charger le morceau de page demandé.
    <Suspense fallback={<PageLoader />}>
      <Routes>
        {/* Pages publiques : suivi acheteur, vente, livreurs & parrainage, mini-vitrines */}
        <Route path="/suivi/:id" element={<SuiviPage />} />
        <Route path="/b/:identifier" element={<BoutiqueClientPage />} />
        <Route path="/b" element={<BoutiqueClientPage />} />
        <Route path="/boutique/:identifier" element={<BoutiqueClientPage />} />
        <Route path="/boutique" element={<BoutiqueClientPage />} />
        <Route path="/vente" element={<VentePage />} />
        <Route path="/livreurs" element={<LivreurPage />} />
        <Route path="/devenir-livreur" element={<LivreurPage />} />
        <Route path="/parrainage" element={<ParrainagePage />} />
        <Route
          path="/login"
          element={user ? <Navigate to="/" replace /> : <LoginPage />}
        />

        {/* Espace connecté (Admin, Marchand & Livreur) */}
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
              element={
                user.role === 'Vendor' ? (
                  <VendorDashboardPage />
                ) : user.role === 'Rider' ? (
                  <RiderDashboardPage />
                ) : (
                  <DashboardPage />
                )
              }
            />
          )}
          <Route path="/vendor/dashboard" element={<RolePage roles={['Admin', 'Vendor']}><VendorDashboardPage /></RolePage>} />
          <Route path="/rider/dashboard" element={<RolePage roles={['Admin', 'Rider']}><RiderDashboardPage /></RolePage>} />
          <Route path="/packs" element={<PacksPage />} />
          <Route path="/transactions" element={<RolePage roles={['Admin']}><TransactionsPage /></RolePage>} />
          <Route path="/vendors" element={<RolePage roles={['Admin']}><VendorsPage /></RolePage>} />
          <Route path="/catalogue" element={<RolePage roles={['Admin', 'Vendor']}><CataloguePage /></RolePage>} />
          <Route path="/leads" element={<RolePage roles={['Admin']}><LeadsPage /></RolePage>} />
          <Route path="/claims" element={<RolePage roles={['Admin']}><ClaimsPage /></RolePage>} />
          <Route path="/avis" element={<RolePage roles={['Admin']}><RatingsPage /></RolePage>} />
          <Route path="/riders" element={<RolePage roles={['Admin']}><RidersPage /></RolePage>} />
          <Route path="/orders" element={<RolePage roles={['Admin', 'Vendor']}><OrdersPage /></RolePage>} />
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
