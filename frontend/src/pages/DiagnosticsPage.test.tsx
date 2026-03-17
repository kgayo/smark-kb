import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DiagnosticsPage } from './DiagnosticsPage';
import * as client from '../api/client';
import type {
  DiagnosticsSummaryResponse,
  SloStatusResponse,
  SecretsStatusResponse,
  WebhookStatusListResponse,
  DeadLetterListResponse,
} from '../api/types';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  getDiagnosticsSummary: vi.fn(),
  getSloStatus: vi.fn(),
  getSecretsStatus: vi.fn(),
  getAllWebhooks: vi.fn(),
  getDeadLetters: vi.fn(),
}));

const mockedClient = vi.mocked(client);

function renderPage() {
  return render(
    <MemoryRouter>
      <DiagnosticsPage />
    </MemoryRouter>,
  );
}

const mockSummary: DiagnosticsSummaryResponse = {
  totalConnectors: 3,
  enabledConnectors: 2,
  disabledConnectors: 1,
  totalWebhooks: 5,
  activeWebhooks: 3,
  fallbackWebhooks: 1,
  failingWebhooks: 1,
  serviceBusConfigured: true,
  keyVaultConfigured: true,
  openAiConfigured: true,
  searchServiceConfigured: false,
  connectorHealth: [
    {
      connectorId: 'c1',
      name: 'ADO Prod',
      connectorType: 'AzureDevOps',
      status: 'Enabled',
      lastSyncStatus: 'Completed',
      lastSyncAt: '2026-03-15T10:00:00Z',
      webhookCount: 2,
      webhooksInFallback: 1,
      totalFailures: 3,
    },
  ],
};

const mockSlo: SloStatusResponse = {
  targets: {
    answerLatencyP95TargetMs: 8000,
    availabilityTargetPercent: 99.5,
    syncLagP95TargetMinutes: 15,
    noEvidenceRateThreshold: 0.25,
    deadLetterDepthThreshold: 10,
  },
  metrics: { chatLatencyMetric: 'smartkb.chat.latency_ms' },
  dashboardHint: 'Query metrics...',
};

const mockSecrets: SecretsStatusResponse = {
  tenantId: 't1',
  keyVaultConfigured: true,
  openAiKeyConfigured: true,
  openAiModel: 'gpt-4o',
};

const mockWebhooks: WebhookStatusListResponse = {
  subscriptions: [
    {
      id: 'w1',
      connectorId: 'c1',
      connectorName: 'ADO Prod',
      connectorType: 'AzureDevOps',
      eventType: 'workitem.created',
      isActive: true,
      pollingFallbackActive: false,
      consecutiveFailures: 0,
      lastDeliveryAt: '2026-03-15T10:00:00Z',
      nextPollAt: null,
      externalSubscriptionId: 'ext-1',
      createdAt: '2026-03-10T00:00:00Z',
      updatedAt: '2026-03-15T10:00:00Z',
    },
    {
      id: 'w2',
      connectorId: 'c1',
      connectorName: 'ADO Prod',
      connectorType: 'AzureDevOps',
      eventType: 'workitem.updated',
      isActive: true,
      pollingFallbackActive: true,
      consecutiveFailures: 3,
      lastDeliveryAt: '2026-03-14T10:00:00Z',
      nextPollAt: '2026-03-15T11:00:00Z',
      externalSubscriptionId: 'ext-2',
      createdAt: '2026-03-10T00:00:00Z',
      updatedAt: '2026-03-15T10:00:00Z',
    },
  ],
  totalCount: 2,
  activeCount: 1,
  fallbackCount: 1,
};

const mockDeadLetters: DeadLetterListResponse = {
  messages: [
    {
      messageId: 'msg-001',
      correlationId: 'corr-001',
      subject: 'sync-job',
      deadLetterReason: 'MaxDeliveryCountExceeded',
      deadLetterErrorDescription: 'Delivery count exceeded',
      deliveryCount: 10,
      enqueuedTime: '2026-03-15T09:00:00Z',
      body: '{"connectorId":"c1"}',
      applicationProperties: {},
    },
  ],
  count: 1,
};

function setupAdminUser() {
  mockedClient.getMe.mockResolvedValue({
    userId: 'u1',
    name: 'Admin',
    tenantId: 't1',
    correlationId: null,
    roles: ['Admin'],
  });
}

describe('DiagnosticsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state initially', () => {
    mockedClient.getMe.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByTestId('diag-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Agent',
      tenantId: 't1',
      correlationId: null,
      roles: ['SupportAgent'],
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('diag-denied')).toBeInTheDocument();
    });
  });

  it('renders overview tab with summary cards', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('overview-panel')).toBeInTheDocument();
    });

    expect(screen.getByTestId('card-connectors')).toBeInTheDocument();
    expect(screen.getByTestId('card-webhooks')).toBeInTheDocument();
    expect(screen.getByTestId('card-services')).toBeInTheDocument();
  });

  it('renders SLO targets section', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('slo-targets')).toBeInTheDocument();
    });
    expect(screen.getByText('8000ms')).toBeInTheDocument();
    expect(screen.getByText('99.5%')).toBeInTheDocument();
  });

  it('renders connector health table', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('connector-health')).toBeInTheDocument();
    });
    expect(screen.getByText('ADO Prod')).toBeInTheDocument();
  });

  it('switches to webhooks tab and loads data', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);
    mockedClient.getAllWebhooks.mockResolvedValue(mockWebhooks);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('overview-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-webhooks'));
    await waitFor(() => {
      expect(screen.getByTestId('webhook-panel')).toBeInTheDocument();
    });
    expect(screen.getByTestId('webhook-table')).toBeInTheDocument();
  });

  it('shows webhook status badges correctly', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);
    mockedClient.getAllWebhooks.mockResolvedValue(mockWebhooks);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('overview-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-webhooks'));
    await waitFor(() => {
      expect(screen.getByText('Healthy')).toBeInTheDocument();
      expect(screen.getByText('Fallback')).toBeInTheDocument();
    });
  });

  it('switches to dead-letters tab and loads data', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);
    mockedClient.getDeadLetters.mockResolvedValue(mockDeadLetters);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('overview-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-dead-letters'));
    await waitFor(() => {
      expect(screen.getByTestId('dead-letter-panel')).toBeInTheDocument();
    });
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('expands dead-letter details on click', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);
    mockedClient.getDeadLetters.mockResolvedValue(mockDeadLetters);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('overview-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-dead-letters'));
    await waitFor(() => {
      expect(screen.getByTestId('dead-letter-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('dl-expand-msg-001'));
    expect(screen.getByTestId('dl-detail-msg-001')).toBeInTheDocument();
    expect(screen.getByText('Delivery count exceeded')).toBeInTheDocument();
  });

  it('shows empty state for dead letters when none exist', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);
    mockedClient.getDeadLetters.mockResolvedValue({ messages: [], count: 0 });

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('overview-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-dead-letters'));
    await waitFor(() => {
      expect(screen.getByTestId('dl-empty')).toBeInTheDocument();
    });
  });

  it('shows error banner on API failure', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockRejectedValue(new Error('Network error'));

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('diag-error')).toBeInTheDocument();
    });
    expect(screen.getByText('Network error')).toBeInTheDocument();
  });

  it('renders tab navigation', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('diag-tabs')).toBeInTheDocument();
    });
    expect(screen.getByTestId('tab-overview')).toBeInTheDocument();
    expect(screen.getByTestId('tab-webhooks')).toBeInTheDocument();
    expect(screen.getByTestId('tab-dead-letters')).toBeInTheDocument();
  });

  it('shows secrets status with model name', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('secrets-status')).toBeInTheDocument();
    });
    expect(screen.getByText('Model: gpt-4o')).toBeInTheDocument();
  });
});
