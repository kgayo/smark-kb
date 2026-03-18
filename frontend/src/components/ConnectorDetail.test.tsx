import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ConnectorDetail } from './ConnectorDetail';
import * as api from '../api/client';
import type { ConnectorResponse } from '../api/types';

vi.mock('../api/client', () => ({
  updateConnector: vi.fn(),
  testConnection: vi.fn(),
  enableConnector: vi.fn(),
  disableConnector: vi.fn(),
  syncNow: vi.fn(),
  deleteConnector: vi.fn(),
  listSyncRuns: vi.fn(),
}));

const mockedApi = vi.mocked(api);

const baseConnector: ConnectorResponse = {
  id: 'c1',
  name: 'ADO Prod',
  connectorType: 'AzureDevOps',
  status: 'Enabled',
  authType: 'Pat',
  hasSecret: true,
  sourceConfig: '{"org": "contoso"}',
  fieldMapping: null,
  scheduleCron: '0 */6 * * *',
  createdAt: '2026-03-10T00:00:00Z',
  updatedAt: '2026-03-15T00:00:00Z',
  lastSyncRun: null,
};

function renderDetail(connector = baseConnector, overrides: Partial<Record<string, any>> = {}) {
  const onBack = overrides.onBack ?? vi.fn();
  const onUpdated = overrides.onUpdated ?? vi.fn();
  const onDeleted = overrides.onDeleted ?? vi.fn();
  return {
    onBack,
    onUpdated,
    onDeleted,
    ...render(
      <ConnectorDetail
        connector={connector}
        onBack={onBack}
        onUpdated={onUpdated}
        onDeleted={onDeleted}
      />,
    ),
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedApi.listSyncRuns.mockResolvedValue({ syncRuns: [] });
});

describe('ConnectorDetail', () => {
  it('renders connector name and status badge', () => {
    renderDetail();
    expect(screen.getByText('ADO Prod')).toBeInTheDocument();
    expect(screen.getByText('Enabled')).toBeInTheDocument();
  });

  it('renders info grid with type, auth, secret, created', () => {
    renderDetail();
    expect(screen.getByText('AzureDevOps')).toBeInTheDocument();
    expect(screen.getByText('Pat')).toBeInTheDocument();
    expect(screen.getByText('Configured')).toBeInTheDocument();
  });

  it('shows "Not set" when hasSecret is false', () => {
    renderDetail({ ...baseConnector, hasSecret: false });
    expect(screen.getByText('Not set')).toBeInTheDocument();
  });

  it('displays source config in read-only mode', () => {
    renderDetail();
    expect(screen.getByTestId('source-config-display')).toHaveTextContent('{"org": "contoso"}');
  });

  it('displays schedule in read-only mode', () => {
    renderDetail();
    expect(screen.getByText(/0 \*\/6 \* \* \*/)).toBeInTheDocument();
  });

  it('calls onBack when back button clicked', () => {
    const { onBack } = renderDetail();
    fireEvent.click(screen.getByTestId('back-btn'));
    expect(onBack).toHaveBeenCalled();
  });

  // ── Test Connection ──

  it('calls testConnection and displays success result', async () => {
    mockedApi.testConnection.mockResolvedValue({
      success: true,
      message: 'Connected OK',
      diagnosticDetail: null,
    });
    renderDetail();
    fireEvent.click(screen.getByTestId('test-connection-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('test-result')).toBeInTheDocument();
      expect(screen.getByText('Connection successful')).toBeInTheDocument();
      expect(screen.getByText('Connected OK')).toBeInTheDocument();
    });
  });

  it('displays test failure result', async () => {
    mockedApi.testConnection.mockResolvedValue({
      success: false,
      message: 'Auth failed',
      diagnosticDetail: 'PAT expired',
    });
    renderDetail();
    fireEvent.click(screen.getByTestId('test-connection-btn'));

    await waitFor(() => {
      expect(screen.getByText('Connection failed')).toBeInTheDocument();
      expect(screen.getByText('PAT expired')).toBeInTheDocument();
    });
  });

  it('shows error banner on test connection API error', async () => {
    mockedApi.testConnection.mockRejectedValue(new Error('Timeout'));
    renderDetail();
    fireEvent.click(screen.getByTestId('test-connection-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('detail-error')).toHaveTextContent('Timeout');
    });
  });

  it('shows Testing... while test in progress', async () => {
    mockedApi.testConnection.mockReturnValue(new Promise(() => {}));
    renderDetail();
    fireEvent.click(screen.getByTestId('test-connection-btn'));
    expect(screen.getByTestId('test-connection-btn')).toHaveTextContent('Testing...');
  });

  // ── Toggle Status ──

  it('calls disableConnector when enabled connector is toggled', async () => {
    const disabled = { ...baseConnector, status: 'Disabled' as const };
    mockedApi.disableConnector.mockResolvedValue(disabled);

    const { onUpdated } = renderDetail();
    fireEvent.click(screen.getByTestId('toggle-status-btn'));

    await waitFor(() => {
      expect(mockedApi.disableConnector).toHaveBeenCalledWith('c1');
      expect(onUpdated).toHaveBeenCalledWith(disabled);
    });
  });

  it('calls enableConnector when disabled connector is toggled', async () => {
    const disabledConnector = { ...baseConnector, status: 'Disabled' as const };
    const enabled = { ...baseConnector, status: 'Enabled' as const };
    mockedApi.enableConnector.mockResolvedValue(enabled);

    const { onUpdated } = renderDetail(disabledConnector);
    fireEvent.click(screen.getByTestId('toggle-status-btn'));

    await waitFor(() => {
      expect(mockedApi.enableConnector).toHaveBeenCalledWith('c1');
      expect(onUpdated).toHaveBeenCalledWith(enabled);
    });
  });

  it('shows Disable button text for enabled connector', () => {
    renderDetail();
    expect(screen.getByTestId('toggle-status-btn')).toHaveTextContent('Disable');
  });

  it('shows Enable button text for disabled connector', () => {
    renderDetail({ ...baseConnector, status: 'Disabled' });
    expect(screen.getByTestId('toggle-status-btn')).toHaveTextContent('Enable');
  });

  // ── Sync Now / Backfill ──

  it('triggers sync now with isBackfill=false', async () => {
    mockedApi.syncNow.mockResolvedValue({ syncRunId: 'sr1', status: 'Pending' });
    mockedApi.listSyncRuns.mockResolvedValue({ syncRuns: [] });
    renderDetail();
    fireEvent.click(screen.getByTestId('sync-now-btn'));

    await waitFor(() => {
      expect(mockedApi.syncNow).toHaveBeenCalledWith('c1', { isBackfill: false });
    });
  });

  it('triggers backfill with isBackfill=true', async () => {
    mockedApi.syncNow.mockResolvedValue({ syncRunId: 'sr1', status: 'Pending' });
    mockedApi.listSyncRuns.mockResolvedValue({ syncRuns: [] });
    renderDetail();
    fireEvent.click(screen.getByTestId('backfill-btn'));

    await waitFor(() => {
      expect(mockedApi.syncNow).toHaveBeenCalledWith('c1', { isBackfill: true });
    });
  });

  it('disables sync buttons when connector is disabled', () => {
    renderDetail({ ...baseConnector, status: 'Disabled' });
    expect(screen.getByTestId('sync-now-btn')).toBeDisabled();
    expect(screen.getByTestId('backfill-btn')).toBeDisabled();
  });

  // ── Edit Mode ──

  it('enters edit mode on Edit button click', () => {
    renderDetail();
    fireEvent.click(screen.getByTestId('edit-btn'));
    expect(screen.getByTestId('edit-section')).toBeInTheDocument();
    expect(screen.getByTestId('edit-name')).toHaveValue('ADO Prod');
  });

  it('saves changes and exits edit mode', async () => {
    const updated = { ...baseConnector, name: 'ADO Staging' };
    mockedApi.updateConnector.mockResolvedValue(updated);
    const { onUpdated } = renderDetail();

    fireEvent.click(screen.getByTestId('edit-btn'));
    fireEvent.change(screen.getByTestId('edit-name'), { target: { value: 'ADO Staging' } });
    fireEvent.click(screen.getByTestId('save-btn'));

    await waitFor(() => {
      expect(mockedApi.updateConnector).toHaveBeenCalledWith('c1', expect.objectContaining({ name: 'ADO Staging' }));
      expect(onUpdated).toHaveBeenCalledWith(updated);
    });
    // Should exit edit mode
    await waitFor(() => {
      expect(screen.queryByTestId('edit-section')).not.toBeInTheDocument();
    });
  });

  it('shows error on save failure', async () => {
    mockedApi.updateConnector.mockRejectedValue(new Error('Validation error'));
    renderDetail();

    fireEvent.click(screen.getByTestId('edit-btn'));
    fireEvent.change(screen.getByTestId('edit-name'), { target: { value: 'X' } });
    fireEvent.click(screen.getByTestId('save-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('detail-error')).toHaveTextContent('Validation error');
    });
  });

  // ── Delete ──

  it('shows confirmation on delete click', () => {
    renderDetail();
    fireEvent.click(screen.getByTestId('delete-btn'));
    expect(screen.getByText('Are you sure?')).toBeInTheDocument();
    expect(screen.getByTestId('confirm-delete-btn')).toBeInTheDocument();
  });

  it('deletes connector on confirmation', async () => {
    mockedApi.deleteConnector.mockResolvedValue(undefined);
    const { onDeleted } = renderDetail();

    fireEvent.click(screen.getByTestId('delete-btn'));
    fireEvent.click(screen.getByTestId('confirm-delete-btn'));

    await waitFor(() => {
      expect(mockedApi.deleteConnector).toHaveBeenCalledWith('c1');
      expect(onDeleted).toHaveBeenCalledWith('c1');
    });
  });

  it('cancels delete confirmation', () => {
    renderDetail();
    fireEvent.click(screen.getByTestId('delete-btn'));
    expect(screen.getByText('Are you sure?')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Cancel'));
    expect(screen.queryByText('Are you sure?')).not.toBeInTheDocument();
  });

  it('shows error on delete failure', async () => {
    mockedApi.deleteConnector.mockRejectedValue(new Error('Cannot delete'));
    renderDetail();

    fireEvent.click(screen.getByTestId('delete-btn'));
    fireEvent.click(screen.getByTestId('confirm-delete-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('detail-error')).toHaveTextContent('Cannot delete');
    });
  });

  // ── Sync Runs ──

  it('loads sync runs on mount', async () => {
    renderDetail();
    await waitFor(() => {
      expect(mockedApi.listSyncRuns).toHaveBeenCalledWith('c1');
    });
  });
});
