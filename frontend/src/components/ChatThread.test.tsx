import { render, screen, fireEvent } from '@testing-library/react';
import { ChatThread } from './ChatThread';
import type { MessageResponse } from '../api/types';
import type { AssistantMeta } from './ChatThread';

const userMsg: MessageResponse = {
  messageId: 'msg-1',
  sessionId: 'sess-1',
  role: 'user',
  content: 'How do I deploy?',
  citations: null,
  confidence: null,
  confidenceLabel: null,
  responseType: null,
  traceId: null,
  correlationId: null,
  createdAt: '2026-03-15T10:00:00Z',
};

const assistantMsg: MessageResponse = {
  messageId: 'msg-2',
  sessionId: 'sess-1',
  role: 'assistant',
  content: 'To deploy, run the pipeline.',
  citations: [
    {
      chunkId: 'c1',
      evidenceId: 'e1',
      title: 'Deploy Guide',
      sourceUrl: 'https://example.com/deploy',
      sourceSystem: 'AzureDevOps',
      snippet: 'Run the deploy pipeline...',
      updatedAt: '2026-03-14T08:00:00Z',
      accessLabel: 'Internal',
    },
  ],
  confidence: 0.82,
  confidenceLabel: 'High',
  responseType: 'final_answer',
  traceId: 'trace-1',
  correlationId: 'corr-1',
  createdAt: '2026-03-15T10:01:00Z',
};

describe('ChatThread', () => {
  it('shows empty state when no messages', () => {
    render(
      <ChatThread messages={[]} loading={false} onShowEvidence={() => {}} metaMap={new Map()} />,
    );
    expect(screen.getByText('Ask a question to get started.')).toBeInTheDocument();
  });

  it('renders user and assistant messages', () => {
    render(
      <ChatThread
        messages={[userMsg, assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={new Map()}
      />,
    );
    expect(screen.getByText('How do I deploy?')).toBeInTheDocument();
    expect(screen.getByText('To deploy, run the pipeline.')).toBeInTheDocument();
  });

  it('shows confidence badge on assistant messages', () => {
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={new Map()}
      />,
    );
    expect(screen.getByTestId('confidence-badge')).toHaveTextContent('High (82%)');
  });

  it('shows citation count button', () => {
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={new Map()}
      />,
    );
    expect(screen.getByTestId('show-citations')).toHaveTextContent('1 source');
  });

  it('calls onShowEvidence when citations clicked', () => {
    const onShow = vi.fn();
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={onShow}
        metaMap={new Map()}
      />,
    );
    fireEvent.click(screen.getByTestId('show-citations'));
    expect(onShow).toHaveBeenCalledWith(assistantMsg.citations);
  });

  it('shows typing indicator when loading', () => {
    render(
      <ChatThread messages={[]} loading={true} onShowEvidence={() => {}} metaMap={new Map()} />,
    );
    expect(screen.getByTestId('typing-indicator')).toBeInTheDocument();
  });

  it('renders next steps from metaMap', () => {
    const meta = new Map<string, AssistantMeta>([
      ['msg-2', { nextSteps: ['Check logs', 'Restart service'], escalation: null }],
    ]);
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={meta}
      />,
    );
    expect(screen.getByTestId('next-steps')).toBeInTheDocument();
    expect(screen.getByText('Check logs')).toBeInTheDocument();
    expect(screen.getByText('Restart service')).toBeInTheDocument();
  });

  it('renders escalation banner when escalation recommended', () => {
    const meta = new Map<string, AssistantMeta>([
      [
        'msg-2',
        {
          nextSteps: [],
          escalation: { recommended: true, targetTeam: 'Engineering', reason: 'Low confidence', handoffNote: '' },
        },
      ],
    ]);
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={meta}
      />,
    );
    expect(screen.getByTestId('escalation-banner')).toBeInTheDocument();
    expect(screen.getByText(/Engineering/)).toBeInTheDocument();
    expect(screen.getByText('Low confidence')).toBeInTheDocument();
  });

  it('does not render escalation banner when not recommended', () => {
    const meta = new Map<string, AssistantMeta>([
      ['msg-2', { nextSteps: [], escalation: { recommended: false, targetTeam: '', reason: '', handoffNote: '' } }],
    ]);
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={meta}
      />,
    );
    expect(screen.queryByTestId('escalation-banner')).not.toBeInTheDocument();
  });

  it('renders escalation CTA button and calls onCreateEscalationDraft', () => {
    const onCreateDraft = vi.fn();
    const meta = new Map<string, AssistantMeta>([
      [
        'msg-2',
        {
          nextSteps: [],
          escalation: { recommended: true, targetTeam: 'Engineering', reason: 'Low confidence', handoffNote: '' },
        },
      ],
    ]);
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        onCreateEscalationDraft={onCreateDraft}
        metaMap={meta}
      />,
    );
    const ctaBtn = screen.getByTestId('create-escalation-draft');
    expect(ctaBtn).toBeInTheDocument();
    expect(ctaBtn).toHaveTextContent('Create escalation draft');
    fireEvent.click(ctaBtn);
    expect(onCreateDraft).toHaveBeenCalledWith('msg-2');
  });

  it('does not render CTA button when onCreateEscalationDraft not provided', () => {
    const meta = new Map<string, AssistantMeta>([
      [
        'msg-2',
        {
          nextSteps: [],
          escalation: { recommended: true, targetTeam: 'Engineering', reason: 'Low confidence', handoffNote: '' },
        },
      ],
    ]);
    render(
      <ChatThread
        messages={[assistantMsg]}
        loading={false}
        onShowEvidence={() => {}}
        metaMap={meta}
      />,
    );
    expect(screen.queryByTestId('create-escalation-draft')).not.toBeInTheDocument();
  });
});
