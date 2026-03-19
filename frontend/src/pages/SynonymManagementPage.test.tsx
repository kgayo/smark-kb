import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SynonymManagementPage } from './SynonymManagementPage';
import * as client from '../api/client';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  listSynonymRules: vi.fn(),
  createSynonymRule: vi.fn(),
  updateSynonymRule: vi.fn(),
  deleteSynonymRule: vi.fn(),
  syncSynonymMaps: vi.fn(),
  seedSynonymRules: vi.fn(),
}));

const mockedClient = vi.mocked(client);

function renderWithRouter() {
  return render(
    <MemoryRouter>
      <SynonymManagementPage />
    </MemoryRouter>,
  );
}

describe('SynonymManagementPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state initially', () => {
    mockedClient.getMe.mockReturnValue(new Promise(() => {}));
    renderWithRouter();
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('shows access denied for non-admin users', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Agent',
      tenantId: 't1',
      correlationId: null,
      roles: ['SupportAgent'],
    });
    renderWithRouter();
    await waitFor(() => {
      expect(screen.getByText(/Access denied/)).toBeInTheDocument();
    });
  });

  it('loads and displays synonym rules for admin users', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Admin',
      tenantId: 't1',
      correlationId: null,
      roles: ['Admin'],
    });
    mockedClient.listSynonymRules.mockResolvedValue({
      rules: [
        {
          id: 'rule-1',
          tenantId: 't1',
          groupName: 'general',
          rule: 'crash, BSOD, blue screen',
          description: 'Crash synonyms',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          updatedAt: '2024-01-01T00:00:00Z',
          createdBy: 'admin-1',
          updatedBy: null,
        },
      ],
      totalCount: 1,
      groups: ['general'],
    });

    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('crash, BSOD, blue screen')).toBeInTheDocument();
    });
    expect(screen.getByText('Crash synonyms')).toBeInTheDocument();
    // "general" appears in both table and dropdown; verify at least one exists
    expect(screen.getAllByText('general').length).toBeGreaterThanOrEqual(1);
  });

  it('shows empty state with seed prompt when no rules exist', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Admin',
      tenantId: 't1',
      correlationId: null,
      roles: ['Admin'],
    });
    mockedClient.listSynonymRules.mockResolvedValue({
      rules: [],
      totalCount: 0,
      groups: [],
    });

    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText(/No synonym rules found/)).toBeInTheDocument();
    });
  });

  it('renders navigation links', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Admin',
      tenantId: 't1',
      correlationId: null,
      roles: ['Admin'],
    });
    mockedClient.listSynonymRules.mockResolvedValue({
      rules: [],
      totalCount: 0,
      groups: [],
    });

    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('Connectors')).toBeInTheDocument();
    });
    expect(screen.getByText('Patterns')).toBeInTheDocument();
    expect(screen.getByText('Diagnostics')).toBeInTheDocument();
    expect(screen.getByText('Chat')).toBeInTheDocument();
  });
});
