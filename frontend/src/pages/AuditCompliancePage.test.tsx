import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AuditCompliancePage } from './AuditCompliancePage';
import { AppRoles } from '../auth/roles';
import * as client from '../api/client';
import type { AuditEventListResponse } from '../api/types';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  queryAuditEvents: vi.fn(),
  exportAuditEvents: vi.fn(),
}));

const mockedClient = vi.mocked(client);

function renderPage() {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <AuditCompliancePage />
    </MemoryRouter>,
  );
}

const mockEvents: AuditEventListResponse = {
  events: [
    {
      eventId: 'evt-001',
      eventType: 'connector.created',
      tenantId: 't1',
      actorId: 'user-1',
      correlationId: 'corr-001',
      timestamp: '2026-03-18T10:30:00Z',
      detail: '{"connectorId":"c1","name":"ADO Prod"}',
    },
    {
      eventId: 'evt-002',
      eventType: 'chat.feedback',
      tenantId: 't1',
      actorId: 'user-2',
      correlationId: 'corr-002',
      timestamp: '2026-03-18T11:00:00Z',
      detail: 'plain text detail',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
  hasMore: false,
};

function setupAdminUser() {
  mockedClient.getMe.mockResolvedValue({
    userId: 'u1',
    name: 'Admin',
    tenantId: 't1',
    correlationId: null,
    roles: [AppRoles.Admin],
  });
}

describe('AuditCompliancePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state initially', () => {
    mockedClient.getMe.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByTestId('audit-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Agent',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.SupportAgent],
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('audit-denied')).toBeInTheDocument();
    });
  });

  it('renders events tab with audit events table', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    expect(screen.getByText('connector.created')).toBeInTheDocument();
    expect(screen.getByText('chat.feedback')).toBeInTheDocument();
    expect(screen.getByText('user-1')).toBeInTheDocument();
    expect(screen.getByText('user-2')).toBeInTheDocument();
    expect(screen.getByLabelText('Toggle details for connector.created event')).toBeInTheDocument();
  });

  it('shows events summary with total count', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-summary')).toBeInTheDocument();
    });
    expect(screen.getByText(/Showing 2 of 2 events/)).toBeInTheDocument();
  });

  it('expands event detail on row click', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('event-row-evt-001'));
    await waitFor(() => {
      expect(screen.getByTestId('event-detail-evt-001')).toBeInTheDocument();
    });
    // JSON detail should be pretty-printed
    expect(screen.getByTestId('event-detail-json')).toBeInTheDocument();
  });

  it('renders plain text detail for non-JSON events', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('event-row-evt-002'));
    await waitFor(() => {
      expect(screen.getByTestId('event-detail-evt-002')).toBeInTheDocument();
    });
    expect(screen.getByTestId('event-detail-raw')).toBeInTheDocument();
    expect(screen.getByText('plain text detail')).toBeInTheDocument();
  });

  it('collapses event detail on second click', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('event-row-evt-001'));
    await waitFor(() => {
      expect(screen.getByTestId('event-detail-evt-001')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('event-row-evt-001'));
    expect(screen.queryByTestId('event-detail-evt-001')).not.toBeInTheDocument();
  });

  it('applies event type filter', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByTestId('filter-event-type'), {
      target: { value: 'connector.created' },
    });

    await waitFor(() => {
      expect(mockedClient.queryAuditEvents).toHaveBeenCalledWith(
        expect.objectContaining({ eventType: 'connector.created', page: 1 }),
      );
    });
  });

  it('paginates with next/previous buttons', async () => {
    setupAdminUser();
    const paginatedEvents: AuditEventListResponse = {
      ...mockEvents,
      totalCount: 100,
      hasMore: true,
    };
    mockedClient.queryAuditEvents.mockResolvedValue(paginatedEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-pagination')).toBeInTheDocument();
    });

    const nextBtn = screen.getByTestId('page-next');
    expect(nextBtn).not.toBeDisabled();
    expect(nextBtn).toHaveAttribute('aria-label', 'Next page');
    expect(screen.getByTestId('page-prev')).toHaveAttribute('aria-label', 'Previous page');
    fireEvent.click(nextBtn);

    await waitFor(() => {
      expect(mockedClient.queryAuditEvents).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2 }),
      );
    });
  });

  it('disables previous button on first page', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-pagination')).toBeInTheDocument();
    });

    expect(screen.getByTestId('page-prev')).toBeDisabled();
  });

  it('shows empty state when no events match filters', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue({
      events: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
      hasMore: false,
    });

    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/No audit events found/)).toBeInTheDocument();
    });
  });

  it('shows error banner on API failure', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockRejectedValue(new Error('Network error'));

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-error')).toBeInTheDocument();
    });
    expect(screen.getByText('Network error')).toBeInTheDocument();
  });

  it('switches to export tab and renders export panel', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-export'));
    await waitFor(() => {
      expect(screen.getByTestId('export-panel')).toBeInTheDocument();
    });
    expect(screen.getByTestId('export-download-btn')).toBeInTheDocument();
    expect(screen.getByTestId('export-download-btn')).toHaveAttribute('aria-label', 'Download NDJSON export');
  });

  it('triggers export download on button click', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);
    mockedClient.exportAuditEvents.mockResolvedValue(new Blob(['{}'], { type: 'application/x-ndjson' }));

    // Mock URL.createObjectURL and URL.revokeObjectURL
    const createObjectURL = vi.fn().mockReturnValue('blob:mock');
    const revokeObjectURL = vi.fn();
    globalThis.URL.createObjectURL = createObjectURL;
    globalThis.URL.revokeObjectURL = revokeObjectURL;

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-export'));
    await waitFor(() => {
      expect(screen.getByTestId('export-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('export-download-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('export-success')).toBeInTheDocument();
    });
    expect(mockedClient.exportAuditEvents).toHaveBeenCalled();
  });

  it('shows export error on failure', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);
    mockedClient.exportAuditEvents.mockRejectedValue(new Error('Export failed'));

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('events-table')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-export'));
    await waitFor(() => {
      expect(screen.getByTestId('export-panel')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('export-download-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('export-error')).toBeInTheDocument();
    });
    expect(screen.getByText('Export failed')).toBeInTheDocument();
  });

  it('renders tab navigation', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('audit-tabs')).toBeInTheDocument();
    });
    expect(screen.getByTestId('tab-events')).toBeInTheDocument();
    expect(screen.getByTestId('tab-export')).toBeInTheDocument();
    expect(screen.getByTestId('tab-events')).toHaveAttribute('aria-label', 'Events tab');
    expect(screen.getByTestId('tab-export')).toHaveAttribute('aria-label', 'Export tab');
  });

  it('renders header navigation links', async () => {
    setupAdminUser();
    mockedClient.queryAuditEvents.mockResolvedValue(mockEvents);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('audit-compliance-page')).toBeInTheDocument();
    });
    expect(screen.getByText('Connectors')).toBeInTheDocument();
    expect(screen.getByText('Diagnostics')).toBeInTheDocument();
    expect(screen.getByText('Privacy')).toBeInTheDocument();
    expect(screen.getByText('Back to Chat')).toBeInTheDocument();
  });
});
