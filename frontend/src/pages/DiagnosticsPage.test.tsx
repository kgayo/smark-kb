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
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
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
      rateLimitHits: 0,
      rateLimitAlerting: false,
    },
  ],
  credentialWarnings: 0,
  credentialCritical: 0,
  credentialExpired: 0,
  rateLimitAlertingConnectors: 0,
};

const mockSlo: SloStatusResponse = {
  targets: {
    answerLatencyP95TargetMs: 8000,
    availabilityTargetPercent: 99.5,
    syncLagP95TargetMinutes: 15,
    noEvidenceRateThreshold: 0.25,
    deadLetterDepthThreshold: 10,
    rateLimitAlertThreshold: 3,
    rateLimitAlertWindowMinutes: 15,
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

    const expandBtn = screen.getByTestId('dl-expand-msg-001');
    expect(expandBtn).toHaveAttribute('aria-label', 'Show dead-letter details');
    fireEvent.click(expandBtn);
    expect(screen.getByTestId('dl-detail-msg-001')).toBeInTheDocument();
    expect(screen.getByText('Delivery count exceeded')).toBeInTheDocument();
    expect(expandBtn).toHaveAttribute('aria-label', 'Hide dead-letter details');
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
    expect(screen.getByTestId('tab-overview')).toHaveAttribute('aria-label', 'Overview tab');
    expect(screen.getByTestId('tab-webhooks')).toHaveAttribute('aria-label', 'Webhooks tab');
    expect(screen.getByTestId('tab-dead-letters')).toHaveAttribute('aria-label', 'Dead Letters tab');
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

  it('shows credentials card as healthy when no warnings', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('card-credentials')).toBeInTheDocument();
    });
    expect(screen.getByText('All healthy')).toBeInTheDocument();
  });

  it('shows credential warnings and expired counts', async () => {
    setupAdminUser();
    const summaryWithCreds: DiagnosticsSummaryResponse = {
      ...mockSummary,
      credentialWarnings: 2,
      credentialCritical: 1,
      credentialExpired: 1,
    };
    mockedClient.getDiagnosticsSummary.mockResolvedValue(summaryWithCreds);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('card-credentials')).toBeInTheDocument();
    });
    expect(screen.getByText('1 expired')).toBeInTheDocument();
    expect(screen.getByText('1 critical')).toBeInTheDocument();
    expect(screen.getByText('2 warning(s)')).toBeInTheDocument();
  });

  it('shows only expired when no warnings or critical', async () => {
    setupAdminUser();
    const summaryExpiredOnly: DiagnosticsSummaryResponse = {
      ...mockSummary,
      credentialWarnings: 0,
      credentialCritical: 0,
      credentialExpired: 3,
    };
    mockedClient.getDiagnosticsSummary.mockResolvedValue(summaryExpiredOnly);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('card-credentials')).toBeInTheDocument();
    });
    expect(screen.getByText('3 expired')).toBeInTheDocument();
    expect(screen.queryByText(/warning/)).not.toBeInTheDocument();
    expect(screen.queryByText(/critical/)).not.toBeInTheDocument();
  });

  it('shows rate-limit card with no alerts when none active', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('card-rate-limits')).toBeInTheDocument();
    });
    expect(screen.getByText('No rate-limit alerts')).toBeInTheDocument();
  });

  it('shows rate-limit alert count when connectors are throttled', async () => {
    setupAdminUser();
    const summaryWithRL: DiagnosticsSummaryResponse = {
      ...mockSummary,
      rateLimitAlertingConnectors: 2,
      connectorHealth: [
        {
          ...mockSummary.connectorHealth[0],
          rateLimitHits: 5,
          rateLimitAlerting: true,
        },
      ],
    };
    mockedClient.getDiagnosticsSummary.mockResolvedValue(summaryWithRL);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('rate-limit-alert-count')).toBeInTheDocument();
    });
    expect(screen.getByText('2 connectors throttled')).toBeInTheDocument();
  });

  it('shows rate-limit badge on connector health row', async () => {
    setupAdminUser();
    const summaryWithRL: DiagnosticsSummaryResponse = {
      ...mockSummary,
      rateLimitAlertingConnectors: 1,
      connectorHealth: [
        {
          ...mockSummary.connectorHealth[0],
          rateLimitHits: 7,
          rateLimitAlerting: true,
        },
      ],
    };
    mockedClient.getDiagnosticsSummary.mockResolvedValue(summaryWithRL);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('rate-limit-badge-c1')).toBeInTheDocument();
    });
    expect(screen.getByText('7 hits')).toBeInTheDocument();
  });

  it('shows rate-limit SLO target row', async () => {
    setupAdminUser();
    mockedClient.getDiagnosticsSummary.mockResolvedValue(mockSummary);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('slo-targets')).toBeInTheDocument();
    });
    expect(screen.getByText('Rate-Limit Alert')).toBeInTheDocument();
    expect(screen.getByText('3 hits / 15min')).toBeInTheDocument();
  });

  it('shows singular connector text for single throttled connector', async () => {
    setupAdminUser();
    const summaryWithRL: DiagnosticsSummaryResponse = {
      ...mockSummary,
      rateLimitAlertingConnectors: 1,
    };
    mockedClient.getDiagnosticsSummary.mockResolvedValue(summaryWithRL);
    mockedClient.getSloStatus.mockResolvedValue(mockSlo);
    mockedClient.getSecretsStatus.mockResolvedValue(mockSecrets);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('rate-limit-alert-count')).toBeInTheDocument();
    });
    expect(screen.getByText('1 connector throttled')).toBeInTheDocument();
  });
});
