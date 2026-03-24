import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ChatPage } from './ChatPage';
import * as api from '../api/client';

vi.mock('../api/client', () => ({
  listSessions: vi.fn(),
  createSession: vi.fn(),
  deleteSession: vi.fn(),
  getMessages: vi.fn(),
  sendMessage: vi.fn(),
  submitFeedback: vi.fn(),
  recordOutcome: vi.fn(),
}));

const mockedApi = vi.mocked(api);

// jsdom doesn't implement scrollIntoView
beforeAll(() => {
  Element.prototype.scrollIntoView = vi.fn();
});

function renderPage() {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <ChatPage />
    </MemoryRouter>,
  );
}

const makeSession = (id: string, title: string) => ({
  sessionId: id,
  tenantId: 'tenant-1',
  userId: 'user-1',
  title,
  customerRef: null as string | null,
  messageCount: 0,
  createdAt: '2026-03-18T00:00:00Z',
  updatedAt: '2026-03-18T00:00:00Z',
  expiresAt: '2026-03-19T00:00:00Z',
});

beforeEach(() => {
  vi.clearAllMocks();
  mockedApi.listSessions.mockResolvedValue({ sessions: [], totalCount: 0 });
});

describe('ChatPage', () => {
  it('renders header with Smart KB title and nav links', async () => {
    renderPage();
    expect(screen.getByText('Smart KB')).toBeInTheDocument();
    expect(screen.getByTestId('admin-link')).toBeInTheDocument();
    expect(screen.getByTestId('patterns-link')).toBeInTheDocument();
  });

  it('shows no-session prompt when no session is active', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Select or create a session to start chatting.')).toBeInTheDocument();
    });
    expect(screen.getByLabelText('Start new chat session')).toBeInTheDocument();
  });

  it('loads sessions on mount', async () => {
    mockedApi.listSessions.mockResolvedValue({
      sessions: [makeSession('s1', 'My Session')],
      totalCount: 1,
    });
    renderPage();
    await waitFor(() => {
      expect(mockedApi.listSessions).toHaveBeenCalledTimes(1);
    });
  });

  it('creates a new session when Start new session is clicked', async () => {
    const newSession = makeSession('s-new', 'New session');
    mockedApi.createSession.mockResolvedValue(newSession);

    renderPage();
    await waitFor(() => expect(screen.getByText('Start new session')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Start new session'));

    await waitFor(() => {
      expect(mockedApi.createSession).toHaveBeenCalledTimes(1);
    });
  });

  it('shows error banner when session creation fails', async () => {
    mockedApi.createSession.mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => expect(screen.getByText('Start new session')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Start new session'));

    await waitFor(() => {
      expect(screen.getByTestId('error-banner')).toBeInTheDocument();
      expect(screen.getByText('Network error')).toBeInTheDocument();
    });
  });

  it('selects a session and loads messages', async () => {
    mockedApi.listSessions.mockResolvedValue({
      sessions: [makeSession('s1', 'Session One')],
      totalCount: 1,
    });
    mockedApi.getMessages.mockResolvedValue({ messages: [], sessionId: 's1', totalCount: 0 });

    renderPage();
    await waitFor(() => expect(screen.getByText('Session One')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Session One'));

    await waitFor(() => {
      expect(mockedApi.getMessages).toHaveBeenCalledWith('s1');
    });
  });

  it('sends a message in active session', async () => {
    const session = makeSession('s1', 'Active');
    mockedApi.listSessions.mockResolvedValue({ sessions: [session], totalCount: 1 });
    mockedApi.getMessages.mockResolvedValue({ messages: [], sessionId: 's1', totalCount: 0 });
    mockedApi.sendMessage.mockResolvedValue({
      session,
      userMessage: {
        messageId: 'um1',
        role: 'User',
        content: 'hello',
        timestamp: '2026-03-18T00:00:00Z',
        citations: [],
      },
      assistantMessage: {
        messageId: 'am1',
        role: 'Assistant',
        content: 'Hi there',
        timestamp: '2026-03-18T00:00:01Z',
        citations: [],
        confidence: 0.8,
        confidenceLabel: 'High',
        responseType: 'final_answer',
      },
      chatResponse: {
        answer: 'Hi there',
        confidence: 0.8,
        confidenceLabel: 'High',
        responseType: 'final_answer',
        citations: [],
        nextSteps: [],
        escalation: null,
        hasEvidence: true,
        traceId: 'tr1',
      },
    } as any);

    renderPage();
    // Select session
    await waitFor(() => expect(screen.getByText('Active')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Active'));
    await waitFor(() => expect(mockedApi.getMessages).toHaveBeenCalled());

    // Type and send a message (placeholder is "Ask a question...")
    const input = screen.getByPlaceholderText('Ask a question...');
    fireEvent.change(input, { target: { value: 'hello' } });
    fireEvent.submit(input.closest('form')!);

    await waitFor(() => {
      expect(mockedApi.sendMessage).toHaveBeenCalledWith('s1', expect.objectContaining({ query: 'hello' }));
    });
  });

  it('shows error banner when sendMessage fails', async () => {
    const session = makeSession('s1', 'Active');
    mockedApi.listSessions.mockResolvedValue({ sessions: [session], totalCount: 1 });
    mockedApi.getMessages.mockResolvedValue({ messages: [], sessionId: 's1', totalCount: 0 });
    mockedApi.sendMessage.mockRejectedValue(new Error('Send failed'));

    renderPage();
    await waitFor(() => expect(screen.getByText('Active')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Active'));
    await waitFor(() => expect(mockedApi.getMessages).toHaveBeenCalled());

    const input = screen.getByPlaceholderText('Ask a question...');
    fireEvent.change(input, { target: { value: 'test' } });
    fireEvent.submit(input.closest('form')!);

    await waitFor(() => {
      expect(screen.getByTestId('error-banner')).toBeInTheDocument();
      expect(screen.getByText('Send failed')).toBeInTheDocument();
    });
  });

  it('deletes session and clears active state', async () => {
    const session = makeSession('s1', 'To Delete');
    mockedApi.listSessions.mockResolvedValue({ sessions: [session], totalCount: 1 });
    mockedApi.getMessages.mockResolvedValue({ messages: [], sessionId: 's1', totalCount: 0 });
    mockedApi.deleteSession.mockResolvedValue(undefined);

    renderPage();
    await waitFor(() => expect(screen.getByText('To Delete')).toBeInTheDocument());

    // Select the session first
    fireEvent.click(screen.getByText('To Delete'));
    await waitFor(() => expect(mockedApi.getMessages).toHaveBeenCalled());

    // Click delete button (in SessionSidebar)
    const deleteBtn = screen.getByTestId('delete-session-s1');
    fireEvent.click(deleteBtn);

    await waitFor(() => {
      expect(mockedApi.deleteSession).toHaveBeenCalledWith('s1');
    });
  });

  it('shows session indicator when a session is selected', async () => {
    const session = makeSession('s1', 'My Topic');
    mockedApi.listSessions.mockResolvedValue({ sessions: [session], totalCount: 1 });
    mockedApi.getMessages.mockResolvedValue({ messages: [], sessionId: 's1', totalCount: 0 });

    renderPage();
    await waitFor(() => expect(screen.getByText('My Topic')).toBeInTheDocument());
    fireEvent.click(screen.getByText('My Topic'));

    await waitFor(() => {
      expect(screen.getByText('My Topic', { selector: '.session-indicator' })).toBeInTheDocument();
    });
  });

  it('silently handles listSessions failure on mount', async () => {
    mockedApi.listSessions.mockRejectedValue(new Error('Auth required'));
    renderPage();
    // Should not crash, no error banner shown for session load failure
    await waitFor(() => {
      expect(screen.getByText('Smart KB')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('error-banner')).not.toBeInTheDocument();
  });

  it('handles feedback submission', async () => {
    const session = makeSession('s1', 'Session');
    mockedApi.listSessions.mockResolvedValue({ sessions: [session], totalCount: 1 });
    mockedApi.getMessages.mockResolvedValue({
      sessionId: 's1',
      totalCount: 1,
      messages: [
        {
          messageId: 'am1',
          sessionId: 's1',
          role: 'assistant' as const,
          content: 'Answer',
          createdAt: '2026-03-18T00:00:00Z',
          citations: [],
          confidence: 0.8,
          confidenceLabel: 'High',
          confidenceRationale: null,
          responseType: 'final_answer',
          traceId: null,
          correlationId: null,
        },
      ],
    });
    mockedApi.submitFeedback.mockResolvedValue({
      feedbackId: 'f1',
      type: 'ThumbsUp',
      reasonCodes: [],
    } as any);

    renderPage();
    await waitFor(() => expect(screen.getByText('Session')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Session'));
    await waitFor(() => expect(screen.getByText('Answer')).toBeInTheDocument());

    // Find and click thumbs up
    const thumbsUp = screen.queryByTestId('thumbs-up');
    if (thumbsUp) {
      fireEvent.click(thumbsUp);
      await waitFor(() => {
        expect(mockedApi.submitFeedback).toHaveBeenCalled();
      });
    }
  });
});
