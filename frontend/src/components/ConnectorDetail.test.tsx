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
  previewConnector: vi.fn(),
  previewRetrieval: vi.fn(),
  validateMapping: vi.fn(),
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
  mockedApi.listSyncRuns.mockResolvedValue({ syncRuns: [], totalCount: 0 });
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

  it('action buttons have descriptive aria-labels', () => {
    renderDetail();
    expect(screen.getByTestId('test-connection-btn')).toHaveAttribute('aria-label', 'Test connection');
    expect(screen.getByTestId('toggle-status-btn')).toHaveAttribute('aria-label', 'Disable connector');
    expect(screen.getByTestId('sync-now-btn')).toHaveAttribute('aria-label', 'Sync connector now');
    expect(screen.getByTestId('backfill-btn')).toHaveAttribute('aria-label', 'Backfill connector data');
    expect(screen.getByTestId('preview-btn')).toHaveAttribute('aria-label', 'Preview connector data');
    expect(screen.getByTestId('edit-btn')).toHaveAttribute('aria-label', 'Edit connector configuration');
    expect(screen.getByTestId('delete-btn')).toHaveAttribute('aria-label', 'Delete connector');
  });

  it('enable connector aria-label for disabled connector', () => {
    renderDetail({ ...baseConnector, status: 'Disabled' });
    expect(screen.getByTestId('toggle-status-btn')).toHaveAttribute('aria-label', 'Enable connector');
  });

  // ── Sync Now / Backfill ──

  it('triggers sync now with isBackfill=false', async () => {
    mockedApi.syncNow.mockResolvedValue({ syncRunId: 'sr1', status: 'Pending' });
    mockedApi.listSyncRuns.mockResolvedValue({ syncRuns: [], totalCount: 0 });
    renderDetail();
    fireEvent.click(screen.getByTestId('sync-now-btn'));

    await waitFor(() => {
      expect(mockedApi.syncNow).toHaveBeenCalledWith('c1', { isBackfill: false });
    });
  });

  it('triggers backfill with isBackfill=true', async () => {
    mockedApi.syncNow.mockResolvedValue({ syncRunId: 'sr1', status: 'Pending' });
    mockedApi.listSyncRuns.mockResolvedValue({ syncRuns: [], totalCount: 0 });
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
    expect(screen.getByLabelText('Connector name')).toBeInTheDocument();
    expect(screen.getByLabelText('Schedule cron expression')).toBeInTheDocument();
    expect(screen.getByLabelText('Save connector changes')).toBeInTheDocument();
    expect(screen.getByLabelText('Cancel editing connector')).toBeInTheDocument();
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

  // ── Preview (P3-027) ──

  it('renders Preview button', () => {
    renderDetail();
    expect(screen.getByTestId('preview-btn')).toBeInTheDocument();
    expect(screen.getByTestId('preview-btn')).toHaveTextContent('Preview');
  });

  it('calls previewConnector on Preview click and displays records', async () => {
    mockedApi.previewConnector.mockResolvedValue({
      records: [
        {
          tenantId: 't1',
          evidenceId: 'ev1',
          title: 'Reset Password',
          textContent: 'Steps to reset your password...',
          sourceType: 'Ticket',
          productArea: 'Auth',
          tags: [],
          author: null,
          severity: null,
        },
      ],
      validationErrors: [],
    });
    renderDetail();
    fireEvent.click(screen.getByTestId('preview-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('preview-records')).toBeInTheDocument();
      expect(screen.getByText('Reset Password')).toBeInTheDocument();
    });
  });

  it('shows preview validation errors', async () => {
    mockedApi.previewConnector.mockResolvedValue({
      records: [],
      validationErrors: ["Record 'ev1': missing required field 'Title'."],
    });
    renderDetail();
    fireEvent.click(screen.getByTestId('preview-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('preview-errors')).toBeInTheDocument();
      expect(screen.getByText(/missing required field/)).toBeInTheDocument();
    });
  });

  it('displays missing-field analysis when field mapping exists', async () => {
    const connectorWithMapping = {
      ...baseConnector,
      fieldMapping: {
        rules: [
          { sourceField: 'title', targetField: 'Title', transform: 'Direct' as const, transformExpression: null, isRequired: true, defaultValue: null, routingTag: null },
        ],
      },
    };
    mockedApi.previewConnector.mockResolvedValue({ records: [], validationErrors: [] });
    mockedApi.validateMapping.mockResolvedValue({
      isValid: true,
      errors: [],
      missingFieldAnalysis: {
        missingRequiredFields: ['TextContent', 'SourceType'],
        fieldCoverage: [
          { fieldName: 'Title', isMapped: true, isRequired: true },
          { fieldName: 'TextContent', isMapped: false, isRequired: true },
          { fieldName: 'SourceType', isMapped: false, isRequired: true },
        ],
      },
    });
    renderDetail(connectorWithMapping);
    fireEvent.click(screen.getByTestId('preview-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('missing-field-analysis')).toBeInTheDocument();
      expect(screen.getByTestId('missing-required-warning')).toBeInTheDocument();
      expect(screen.getByText(/TextContent, SourceType/)).toBeInTheDocument();
    });
  });

  it('shows Previewing... while in progress', async () => {
    mockedApi.previewConnector.mockReturnValue(new Promise(() => {}));
    renderDetail();
    fireEvent.click(screen.getByTestId('preview-btn'));
    expect(screen.getByTestId('preview-btn')).toHaveTextContent('Previewing...');
  });

  it('shows error banner on preview API error', async () => {
    mockedApi.previewConnector.mockRejectedValue(new Error('Preview timeout'));
    renderDetail();
    fireEvent.click(screen.getByTestId('preview-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('detail-error')).toHaveTextContent('Preview timeout');
    });
  });

  // ── Retrieval Test (P3-027) ──

  it('renders retrieval test input and button', () => {
    renderDetail();
    expect(screen.getByTestId('retrieval-query-input')).toBeInTheDocument();
    expect(screen.getByTestId('retrieval-test-btn')).toBeInTheDocument();
  });

  it('retrieval test button is disabled when query is empty', () => {
    renderDetail();
    expect(screen.getByTestId('retrieval-test-btn')).toBeDisabled();
  });

  it('calls previewRetrieval and displays results', async () => {
    mockedApi.previewRetrieval.mockResolvedValue({
      chunks: [
        {
          chunkId: 'ch-1',
          title: 'Password Reset',
          chunkText: 'Reset steps...',
          sourceType: 'Ticket',
          productArea: 'Auth',
          score: 1.0,
          updatedAt: '2026-03-15T00:00:00Z',
        },
      ],
      totalChunksForConnector: 10,
      hasEvidence: true,
      message: null,
    });
    renderDetail();

    fireEvent.change(screen.getByTestId('retrieval-query-input'), {
      target: { value: 'password reset' },
    });
    fireEvent.click(screen.getByTestId('retrieval-test-btn'));

    await waitFor(() => {
      expect(mockedApi.previewRetrieval).toHaveBeenCalledWith('c1', {
        query: 'password reset',
        maxResults: 5,
      });
      expect(screen.getByTestId('retrieval-results')).toBeInTheDocument();
      expect(screen.getByText('Password Reset')).toBeInTheDocument();
      expect(screen.getByText(/1 results from 10 total chunks/)).toBeInTheDocument();
    });
  });

  it('shows retrieval message when no results', async () => {
    mockedApi.previewRetrieval.mockResolvedValue({
      chunks: [],
      totalChunksForConnector: 5,
      hasEvidence: false,
      message: 'No chunks matched the query.',
    });
    renderDetail();

    fireEvent.change(screen.getByTestId('retrieval-query-input'), {
      target: { value: 'xyznonexistent' },
    });
    fireEvent.click(screen.getByTestId('retrieval-test-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('retrieval-message')).toHaveTextContent(
        'No chunks matched the query.',
      );
    });
  });

  it('shows error banner on retrieval test API error', async () => {
    mockedApi.previewRetrieval.mockRejectedValue(new Error('Server error'));
    renderDetail();

    fireEvent.change(screen.getByTestId('retrieval-query-input'), {
      target: { value: 'test' },
    });
    fireEvent.click(screen.getByTestId('retrieval-test-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('detail-error')).toHaveTextContent('Server error');
    });
  });

  it('shows Searching... while retrieval test in progress', async () => {
    mockedApi.previewRetrieval.mockReturnValue(new Promise(() => {}));
    renderDetail();

    fireEvent.change(screen.getByTestId('retrieval-query-input'), {
      target: { value: 'test' },
    });
    fireEvent.click(screen.getByTestId('retrieval-test-btn'));

    expect(screen.getByTestId('retrieval-test-btn')).toHaveTextContent('Searching...');
  });

  it('logs warning when sync run load fails', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    mockedApi.listSyncRuns.mockRejectedValue(new Error('Sync fetch failed'));
    renderDetail();

    await waitFor(() => {
      expect(warnSpy).toHaveBeenCalledWith(
        '[ConnectorDetail] Failed to load sync runs:',
        expect.any(Error),
      );
    });
    warnSpy.mockRestore();
  });

  it('has aria-labels on back and cancel delete buttons', () => {
    renderDetail();
    expect(screen.getByLabelText('Back to connector list')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('delete-btn'));
    expect(screen.getByLabelText('Cancel delete')).toBeInTheDocument();
  });
});
