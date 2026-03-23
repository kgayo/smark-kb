import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { ChatPage } from './pages/ChatPage';
import { AdminPage } from './pages/AdminPage';
import { PatternGovernancePage } from './pages/PatternGovernancePage';
import { DiagnosticsPage } from './pages/DiagnosticsPage';
import { SynonymManagementPage } from './pages/SynonymManagementPage';
import { RoutingAnalyticsPage } from './pages/RoutingAnalyticsPage';
import { PlaybooksPage } from './pages/PlaybooksPage';
import { CostControlsPage } from './pages/CostControlsPage';
import { PrivacyAdminPage } from './pages/PrivacyAdminPage';
import { AuditCompliancePage } from './pages/AuditCompliancePage';
import { GoldDatasetPage } from './pages/GoldDatasetPage';

export function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
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
      </AuthProvider>
    </BrowserRouter>
  );
}
