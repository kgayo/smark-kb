import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PrivacyAdminPage } from './PrivacyAdminPage';
import * as api from '../api/client';

vi.mock('../api/client', () => ({
  getPiiPolicy: vi.fn(),
  updatePiiPolicy: vi.fn(),
  resetPiiPolicy: vi.fn(),
  getRetentionPolicies: vi.fn(),
  updateRetentionPolicy: vi.fn(),
  deleteRetentionPolicy: vi.fn(),
  runRetentionCleanup: vi.fn(),
  listDeletionRequests: vi.fn(),
  createDeletionRequest: vi.fn(),
  getDeletionRequest: vi.fn(),
  getRetentionCompliance: vi.fn(),
  getRetentionHistory: vi.fn(),
}));

vi.mock('../auth/useRoles', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../auth/useRoles')>();
  return { ...actual, useRoles: vi.fn() };
});

import { useRoles } from '../auth/useRoles';
const mockedUseRoles = vi.mocked(useRoles);
const mockedApi = vi.mocked(api);

function renderPage() {
  return render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><PrivacyAdminPage /></MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('PrivacyAdminPage', () => {
  it('shows loading state', () => {
    mockedUseRoles.mockReturnValue({ roles: [], loading: true });
    renderPage();
    expect(screen.getByTestId('privacy-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', () => {
    mockedUseRoles.mockReturnValue({ roles: ['SupportAgent'], loading: false });
    renderPage();
    expect(screen.getByTestId('privacy-denied')).toBeInTheDocument();
  });

  it('loads and displays PII policy', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue({
      tenantId: 't1',
      enforcementMode: 'redact',
      enabledPiiTypes: ['email', 'phone'],
      customPatterns: [],
      auditRedactions: true,
      updatedAt: '2026-03-19T00:00:00Z',
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('redact')).toBeInTheDocument();
      expect(screen.getByText('email, phone')).toBeInTheDocument();
    });
  });

  it('shows no policy message when none configured', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue(null);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/No PII policy configured/)).toBeInTheDocument();
    });
  });

  it('switches to retention tab', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue(null);
    mockedApi.getRetentionPolicies.mockResolvedValue({
      tenantId: 't1',
      policies: [{
        entityType: 'AppSession',
        retentionDays: 90,
        metricRetentionDays: null,
        updatedAt: '2026-03-19T00:00:00Z',
      }],
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getPiiPolicy).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Retention'));
    await waitFor(() => {
      expect(screen.getByText('AppSession')).toBeInTheDocument();
      expect(screen.getByText('90')).toBeInTheDocument();
    });
  });

  it('switches to deletion tab', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue(null);
    mockedApi.listDeletionRequests.mockResolvedValue({
      requests: [{
        requestId: 'del-001',
        tenantId: 't1',
        subjectId: 'user@example.com',
        status: 'Completed',
        requestedAt: '2026-03-19T00:00:00Z',
        completedAt: '2026-03-19T00:01:00Z',
        deletionSummary: { sessions: 3, messages: 12 },
        errorDetail: null,
      }],
      totalCount: 1,
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getPiiPolicy).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Data Deletion'));
    await waitFor(() => {
      expect(screen.getByText('user@example.com')).toBeInTheDocument();
      expect(screen.getByTestId('deletion-table')).toBeInTheDocument();
    });
  });

  it('switches to compliance tab', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue(null);
    mockedApi.getRetentionCompliance.mockResolvedValue({
      tenantId: 't1',
      generatedAt: '2026-03-19T00:00:00Z',
      isCompliant: true,
      totalPolicies: 2,
      overduePolicies: 0,
      entries: [{
        entityType: 'AppSession',
        retentionDays: 90,
        metricRetentionDays: null,
        lastExecutedAt: '2026-03-18T00:00:00Z',
        lastDeletedCount: 5,
        isOverdue: false,
        daysSinceLastExecution: 1,
      }],
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getPiiPolicy).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Compliance'));
    await waitFor(() => {
      expect(screen.getByText('Compliant')).toBeInTheDocument();
      expect(screen.getByText('OK')).toBeInTheDocument();
    });
  });

  it('shows non-compliant status', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue(null);
    mockedApi.getRetentionCompliance.mockResolvedValue({
      tenantId: 't1',
      generatedAt: '2026-03-19T00:00:00Z',
      isCompliant: false,
      totalPolicies: 1,
      overduePolicies: 1,
      entries: [{
        entityType: 'Message',
        retentionDays: 30,
        metricRetentionDays: null,
        lastExecutedAt: null,
        lastDeletedCount: null,
        isOverdue: true,
        daysSinceLastExecution: 999,
      }],
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getPiiPolicy).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Compliance'));
    await waitFor(() => {
      expect(screen.getByText('Non-Compliant')).toBeInTheDocument();
      expect(screen.getByTestId('compliance-table')).toBeInTheDocument();
    });
  });

  it('shows error on load failure', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('privacy-error')).toBeInTheDocument();
    });
  });

  it('renders all tabs', () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getPiiPolicy.mockResolvedValue(null);
    renderPage();
    expect(screen.getByText('PII Policy')).toBeInTheDocument();
    expect(screen.getByText('Retention')).toBeInTheDocument();
    expect(screen.getByText('Data Deletion')).toBeInTheDocument();
    expect(screen.getByText('Compliance')).toBeInTheDocument();
  });
});
