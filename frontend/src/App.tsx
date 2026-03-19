import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { ChatPage } from './pages/ChatPage';
import { AdminPage } from './pages/AdminPage';
import { PatternGovernancePage } from './pages/PatternGovernancePage';
import { DiagnosticsPage } from './pages/DiagnosticsPage';
import { SynonymManagementPage } from './pages/SynonymManagementPage';

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
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
