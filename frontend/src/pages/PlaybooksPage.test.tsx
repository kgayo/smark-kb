import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PlaybooksPage } from './PlaybooksPage';
import * as api from '../api/client';

vi.mock('../api/client', () => ({
  listPlaybooks: vi.fn(),
  getPlaybook: vi.fn(),
  createPlaybook: vi.fn(),
  updatePlaybook: vi.fn(),
  deletePlaybook: vi.fn(),
}));

vi.mock('../auth/useRoles', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../auth/useRoles')>();
  return { ...actual, useRoles: vi.fn() };
});

import { useRoles } from '../auth/useRoles';
const mockedUseRoles = vi.mocked(useRoles);
const mockedApi = vi.mocked(api);

function renderPage() {
  return render(<MemoryRouter><PlaybooksPage /></MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
});

const samplePlaybook = {
  id: 'pb1',
  teamName: 'Engineering',
  description: 'Eng team playbook',
  requiredFields: ['title', 'severity'],
  checklist: ['Check logs', 'Verify repro'],
  contactChannel: '#eng-oncall',
  requiresApproval: true,
  minSeverity: 'P2',
  autoRouteSeverity: null,
  maxConcurrentEscalations: 5,
  fallbackTeam: 'Platform',
  isActive: true,
  createdAt: '2026-03-19T00:00:00Z',
  updatedAt: '2026-03-19T00:00:00Z',
};

describe('PlaybooksPage', () => {
  it('shows loading state', () => {
    mockedUseRoles.mockReturnValue({ roles: [], loading: true });
    renderPage();
    expect(screen.getByTestId('playbooks-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', () => {
    mockedUseRoles.mockReturnValue({ roles: ['SupportAgent'], loading: false });
    renderPage();
    expect(screen.getByTestId('playbooks-denied')).toBeInTheDocument();
  });

  it('loads and displays playbook list', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.listPlaybooks.mockResolvedValue({
      playbooks: [samplePlaybook],
      totalCount: 1,
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Engineering')).toBeInTheDocument();
      expect(screen.getByText('Eng team playbook')).toBeInTheDocument();
    });
  });

  it('shows empty state', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.listPlaybooks.mockResolvedValue({ playbooks: [], totalCount: 0 });
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('No playbooks configured.')).toBeInTheDocument();
    });
  });

  it('navigates to playbook detail on click', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.listPlaybooks.mockResolvedValue({ playbooks: [samplePlaybook], totalCount: 1 });
    mockedApi.getPlaybook.mockResolvedValue(samplePlaybook);
    renderPage();
    await waitFor(() => expect(screen.getByText('Engineering')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Engineering'));
    await waitFor(() => {
      expect(screen.getByTestId('playbook-detail')).toBeInTheDocument();
      expect(screen.getByText('#eng-oncall')).toBeInTheDocument();
      expect(screen.getByText('Check logs')).toBeInTheDocument();
    });
  });

  it('shows create form', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.listPlaybooks.mockResolvedValue({ playbooks: [], totalCount: 0 });
    renderPage();
    await waitFor(() => expect(screen.getByTestId('new-playbook-btn')).toBeInTheDocument());

    fireEvent.click(screen.getByTestId('new-playbook-btn'));
    expect(screen.getByTestId('create-playbook-form')).toBeInTheDocument();
    expect(screen.getByText('Create Playbook')).toBeInTheDocument();
  });

  it('shows error on load failure', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.listPlaybooks.mockRejectedValue(new Error('Failed'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('playbooks-error')).toBeInTheDocument();
    });
  });

  it('renders navigation links', () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.listPlaybooks.mockResolvedValue({ playbooks: [], totalCount: 0 });
    renderPage();
    expect(screen.getByText('Connectors')).toBeInTheDocument();
    expect(screen.getByText('Routing')).toBeInTheDocument();
  });
});
