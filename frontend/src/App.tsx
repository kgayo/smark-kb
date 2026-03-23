import { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { ErrorBoundary } from './components/ErrorBoundary';
import { ChatPage } from './pages/ChatPage';

const AdminPage = lazy(() => import('./pages/AdminPage').then(m => ({ default: m.AdminPage })));
const PatternGovernancePage = lazy(() => import('./pages/PatternGovernancePage').then(m => ({ default: m.PatternGovernancePage })));
const DiagnosticsPage = lazy(() => import('./pages/DiagnosticsPage').then(m => ({ default: m.DiagnosticsPage })));
const SynonymManagementPage = lazy(() => import('./pages/SynonymManagementPage').then(m => ({ default: m.SynonymManagementPage })));
const RoutingAnalyticsPage = lazy(() => import('./pages/RoutingAnalyticsPage').then(m => ({ default: m.RoutingAnalyticsPage })));
const PlaybooksPage = lazy(() => import('./pages/PlaybooksPage').then(m => ({ default: m.PlaybooksPage })));
const CostControlsPage = lazy(() => import('./pages/CostControlsPage').then(m => ({ default: m.CostControlsPage })));
const PrivacyAdminPage = lazy(() => import('./pages/PrivacyAdminPage').then(m => ({ default: m.PrivacyAdminPage })));
const AuditCompliancePage = lazy(() => import('./pages/AuditCompliancePage').then(m => ({ default: m.AuditCompliancePage })));
const GoldDatasetPage = lazy(() => import('./pages/GoldDatasetPage').then(m => ({ default: m.GoldDatasetPage })));

export function App() {
  return (
    <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <ErrorBoundary>
        <AuthProvider>
          <Suspense fallback={<div className="loading-page">Loading...</div>}>
            <Routes>
              <Route path="/" element={<ChatPage />} />
              <Route path="/admin" element={<AdminPage />} />
              <Route path="/patterns" element={<PatternGovernancePage />} />
              <Route path="/diagnostics" element={<DiagnosticsPage />} />
              <Route path="/synonyms" element={<SynonymManagementPage />} />
              <Route path="/routing" element={<RoutingAnalyticsPage />} />
              <Route path="/playbooks" element={<PlaybooksPage />} />
              <Route path="/cost" element={<CostControlsPage />} />
              <Route path="/privacy" element={<PrivacyAdminPage />} />
              <Route path="/audit" element={<AuditCompliancePage />} />
              <Route path="/gold-cases" element={<GoldDatasetPage />} />
            </Routes>
          </Suspense>
        </AuthProvider>
      </ErrorBoundary>
    </BrowserRouter>
  );
}
