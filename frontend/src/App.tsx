import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { ChatPage } from './pages/ChatPage';
import { AdminPage } from './pages/AdminPage';
import { PatternGovernancePage } from './pages/PatternGovernancePage';
import { DiagnosticsPage } from './pages/DiagnosticsPage';

export function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/" element={<ChatPage />} />
          <Route path="/admin" element={<AdminPage />} />
          <Route path="/patterns" element={<PatternGovernancePage />} />
          <Route path="/diagnostics" element={<DiagnosticsPage />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
