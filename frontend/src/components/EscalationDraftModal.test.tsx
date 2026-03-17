import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { EscalationDraftModal } from './EscalationDraftModal';
import type { CitationDto, EscalationSignal, EscalationDraftResponse } from '../api/types';
import * as api from '../api/client';

vi.mock('../api/client');

const mockEscalation: EscalationSignal = {
  recommended: true,
  targetTeam: 'Engineering',
  reason: 'Low confidence on auth issue',
  handoffNote: 'Customer experiencing repeated auth failures',
};

const mockCitation: CitationDto = {
  chunkId: 'c1',
  evidenceId: 'e1',
  title: 'Auth Guide',
  sourceUrl: 'https://example.com/auth',
  sourceSystem: 'AzureDevOps',
  snippet: 'Configure auth settings...',
  updatedAt: '2026-03-14T08:00:00Z',
  accessLabel: 'Internal',
};

const mockDraftResponse: EscalationDraftResponse = {
  draftId: 'draft-1',
  sessionId: 'sess-1',
  messageId: 'msg-1',
  title: 'Escalation: Low confidence on auth issue',
  customerSummary: '',
  stepsToReproduce: '',
  logsIdsRequested: '',
  suspectedComponent: '',
  severity: 'P3',
  evidenceLinks: [mockCitation],
  targetTeam: 'Engineering',
  reason: 'Low confidence on auth issue',
  createdAt: '2026-03-15T10:00:00Z',
  exportedAt: null,
  approvedAt: null,
  externalId: null,
  externalUrl: null,
  externalStatus: null,
  externalErrorDetail: null,
  targetConnectorType: null,
};

describe('EscalationDraftModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(api.createEscalationDraft).mockResolvedValue(mockDraftResponse);
    vi.mocked(api.updateEscalationDraft).mockResolvedValue(mockDraftResponse);
    vi.mocked(api.exportEscalationDraft).mockResolvedValue({
      draftId: 'draft-1',
      markdown: '# Escalation\n\nSome content',
      exportedAt: '2026-03-15T10:05:00Z',
    });
    vi.mocked(api.listConnectors).mockResolvedValue({ connectors: [], totalCount: 0 });
  });

  it('does not render when closed', () => {
    render(
      <EscalationDraftModal
        open={false}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    expect(screen.queryByTestId('escalation-draft-modal')).not.toBeInTheDocument();
  });

  it('renders modal and calls createEscalationDraft on open', async () => {
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    expect(screen.getByTestId('escalation-draft-modal')).toBeInTheDocument();
    await waitFor(() => {
      expect(api.createEscalationDraft).toHaveBeenCalledWith(
        expect.objectContaining({
          sessionId: 'sess-1',
          messageId: 'msg-1',
          targetTeam: 'Engineering',
          reason: 'Low confidence on auth issue',
        }),
      );
    });
  });

  it('displays form fields after draft creation', async () => {
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-title')).toBeInTheDocument();
    });
    expect(screen.getByTestId('draft-severity')).toBeInTheDocument();
    expect(screen.getByTestId('draft-target-team')).toBeInTheDocument();
    expect(screen.getByTestId('draft-reason')).toBeInTheDocument();
    expect(screen.getByTestId('draft-customer-summary')).toBeInTheDocument();
    expect(screen.getByTestId('draft-suspected-component')).toBeInTheDocument();
    expect(screen.getByTestId('draft-steps-to-reproduce')).toBeInTheDocument();
    expect(screen.getByTestId('draft-logs-ids')).toBeInTheDocument();
  });

  it('shows evidence count when citations present', async () => {
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-evidence-count')).toHaveTextContent(
        '1 evidence link attached',
      );
    });
  });

  it('calls updateEscalationDraft on save', async () => {
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-save')).toBeInTheDocument();
    });

    // Edit a field
    fireEvent.change(screen.getByTestId('draft-customer-summary'), {
      target: { value: 'Customer cannot log in' },
    });

    fireEvent.click(screen.getByTestId('draft-save'));

    await waitFor(() => {
      expect(api.updateEscalationDraft).toHaveBeenCalledWith(
        'draft-1',
        expect.objectContaining({
          customerSummary: 'Customer cannot log in',
        }),
      );
    });
  });

  it('calls exportEscalationDraft and copies to clipboard on copy button', async () => {
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    });

    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-copy-markdown')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('draft-copy-markdown'));

    await waitFor(() => {
      expect(api.exportEscalationDraft).toHaveBeenCalledWith('draft-1');
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
        '# Escalation\n\nSome content',
      );
    });
  });

  it('shows "Copied!" after successful copy', async () => {
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    });

    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-copy-markdown')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('draft-copy-markdown'));

    await waitFor(() => {
      expect(screen.getByTestId('draft-copy-markdown')).toHaveTextContent('Copied!');
    });
  });

  it('calls onClose when close button clicked', async () => {
    const onClose = vi.fn();
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={onClose}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('escalation-draft-close')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('escalation-draft-close'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('shows error when draft creation fails', async () => {
    vi.mocked(api.createEscalationDraft).mockRejectedValue(
      new Error('Failed to create'),
    );

    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('escalation-draft-error')).toHaveTextContent(
        'Failed to create',
      );
    });
  });

  it('disables ADO and ClickUp buttons when no connectors available', async () => {
    vi.mocked(api.listConnectors).mockResolvedValue({ connectors: [], totalCount: 0 });

    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-create-ado')).toBeInTheDocument();
    });

    const adoBtn = screen.getByTestId('draft-create-ado');
    const clickupBtn = screen.getByTestId('draft-create-clickup');
    expect(adoBtn).toBeDisabled();
    expect(clickupBtn).toBeDisabled();
    expect(adoBtn).toHaveAttribute('title', expect.stringContaining('No ADO or ClickUp connectors configured'));
    expect(clickupBtn).toHaveAttribute('title', expect.stringContaining('No ADO or ClickUp connectors configured'));
  });

  it('pre-fills fields from escalation signal', async () => {
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-target-team')).toHaveValue('Engineering');
    });
    expect(screen.getByTestId('draft-reason')).toHaveValue(
      'Low confidence on auth issue',
    );
  });

  it('allows changing severity via dropdown', async () => {
    render(
      <EscalationDraftModal
        open={true}
        sessionId="sess-1"
        messageId="msg-1"
        escalation={mockEscalation}
        citations={[mockCitation]}
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('draft-severity')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByTestId('draft-severity'), { target: { value: 'P1' } });
    expect(screen.getByTestId('draft-severity')).toHaveValue('P1');
  });
});
