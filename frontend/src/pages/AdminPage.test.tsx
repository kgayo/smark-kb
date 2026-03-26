import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AdminPage } from './AdminPage';
import { AppRoles } from '../auth/roles';
import * as client from '../api/client';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  listConnectors: vi.fn(),
  getConnector: vi.fn(),
  createConnector: vi.fn(),
}));

const mockedClient = vi.mocked(client);

function renderWithRouter() {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <AdminPage />
    </MemoryRouter>,
  );
}

describe('AdminPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state initially', () => {
    mockedClient.getMe.mockReturnValue(new Promise(() => {})); // never resolves
    renderWithRouter();
    expect(screen.getByTestId('admin-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin users', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Agent',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.SupportAgent],
    });
    renderWithRouter();
    await waitFor(() => {
      expect(screen.getByTestId('admin-denied')).toBeInTheDocument();
    });
    expect(screen.getByText('Access Denied')).toBeInTheDocument();
  });

  it('loads and shows connector list for admin users', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Admin User',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.Admin],
    });
    mockedClient.listConnectors.mockResolvedValue({
      connectors: [
        {
          id: 'c1',
          name: 'ADO Prod',
          connectorType: 'AzureDevOps',
          status: 'Enabled',
          authType: 'Pat',
          hasSecret: true,
          sourceConfig: null,
          fieldMapping: null,
          scheduleCron: null,
          createdAt: '2026-03-10T00:00:00Z',
          updatedAt: '2026-03-15T00:00:00Z',
          lastSyncRun: null,
        },
      ],
      totalCount: 1,
    });
    renderWithRouter();
    await waitFor(() => {
      expect(screen.getByText('ADO Prod')).toBeInTheDocument();
    });
  });

  it('shows empty connector list for admin with no connectors', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Admin',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.Admin],
    });
    mockedClient.listConnectors.mockResolvedValue({
      connectors: [],
      totalCount: 0,
    });
    renderWithRouter();
    await waitFor(() => {
      expect(screen.getByTestId('connector-empty')).toBeInTheDocument();
    });
  });

  it('shows back to chat link', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Admin',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.Admin],
    });
    mockedClient.listConnectors.mockResolvedValue({
      connectors: [],
      totalCount: 0,
    });
    renderWithRouter();
    await waitFor(() => {
      expect(screen.getByTestId('back-to-chat')).toBeInTheDocument();
    });
  });
});
