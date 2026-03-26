import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { logger } from '../utils/logger';
import type { CitationDto, EscalationSignal, MessageResponse, RecordOutcomeRequest, RetrievalFilter, SessionResponse, SubmitFeedbackRequest } from '../api/types';
import type { AssistantMeta, FeedbackState } from '../components/ChatThread';
import * as api from '../api/client';
import { SessionSidebar } from '../components/SessionSidebar';
import { ChatThread } from '../components/ChatThread';
import { MessageInput } from '../components/MessageInput';
import { EvidenceDrawer } from '../components/EvidenceDrawer';
import { EscalationDraftModal } from '../components/EscalationDraftModal';
import { OutcomeWidget } from '../components/OutcomeWidget';
import { RetrievalFilterPanel } from '../components/RetrievalFilterPanel';

export function ChatPage() {
  const [sessions, setSessions] = useState<SessionResponse[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [messages, setMessages] = useState<MessageResponse[]>([]);
  const [metaMap, setMetaMap] = useState<Map<string, AssistantMeta>>(() => new Map());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerCitations, setDrawerCitations] = useState<CitationDto[]>([]);
  const [draftModalOpen, setDraftModalOpen] = useState(false);
  const [draftMessageId, setDraftMessageId] = useState<string | null>(null);
  const [draftEscalation, setDraftEscalation] = useState<EscalationSignal | null>(null);
  const [draftCitations, setDraftCitations] = useState<CitationDto[]>([]);
  const [feedbackMap, setFeedbackMap] = useState<Map<string, FeedbackState>>(() => new Map());
  const [outcomeRecorded, setOutcomeRecorded] = useState<{ resolutionType: string } | null>(null);
  const [retrievalFilters, setRetrievalFilters] = useState<RetrievalFilter>({});
  const threadEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    loadSessions();
  }, []);

  useEffect(() => {
    threadEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, loading]);

  async function loadSessions() {
    try {
      const result = await api.listSessions();
      setSessions(result.sessions);
    } catch (err) {
      logger.warn('[ChatPage] Failed to load sessions:', err);
    }
  }

  async function loadMessages(sessionId: string) {
    try {
      const result = await api.getMessages(sessionId);
      setMessages(result.messages);
    } catch (err) {
      logger.warn('[ChatPage] Failed to load messages:', err);
      setMessages([]);
    }
  }

  const handleSelectSession = useCallback(async (sessionId: string) => {
    setActiveSessionId(sessionId);
    setError(null);
    setDrawerOpen(false);
    setMetaMap(new Map());
    setFeedbackMap(new Map());
    setOutcomeRecorded(null);
    await loadMessages(sessionId);
  }, []);

  const handleNewSession = useCallback(async () => {
    try {
      setError(null);
      const session = await api.createSession();
      setSessions((prev) => [session, ...prev]);
      setActiveSessionId(session.sessionId);
      setMessages([]);
      setMetaMap(new Map());
      setFeedbackMap(new Map());
      setOutcomeRecorded(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create session');
    }
  }, []);

  const handleDeleteSession = useCallback(
    async (sessionId: string) => {
      try {
        await api.deleteSession(sessionId);
        setSessions((prev) => prev.filter((s) => s.sessionId !== sessionId));
        if (activeSessionId === sessionId) {
          setActiveSessionId(null);
          setMessages([]);
          setMetaMap(new Map());
          setFeedbackMap(new Map());
          setOutcomeRecorded(null);
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to delete session');
      }
    },
    [activeSessionId],
  );

  const handleSend = useCallback(
    async (query: string) => {
      if (!activeSessionId) return;
      setLoading(true);
      setError(null);

      try {
        const hasFilters = retrievalFilters.sourceTypes?.length ||
          retrievalFilters.productAreas?.length ||
          retrievalFilters.timeHorizonDays ||
          retrievalFilters.tags?.length;
        const result = await api.sendMessage(activeSessionId, {
          query,
          filters: hasFilters ? retrievalFilters : undefined,
        });

        setMessages((prev) => [...prev, result.userMessage, result.assistantMessage]);

        setMetaMap((prev) => {
          const next = new Map(prev);
          next.set(result.assistantMessage.messageId, {
            nextSteps: result.chatResponse.nextSteps,
            escalation: result.chatResponse.escalation
              ? {
                  recommended: result.chatResponse.escalation.recommended,
                  targetTeam: result.chatResponse.escalation.targetTeam,
                  reason: result.chatResponse.escalation.reason,
                  handoffNote: result.chatResponse.escalation.handoffNote,
                }
              : null,
          });
          return next;
        });

        setSessions((prev) =>
          prev.map((s) =>
            s.sessionId === result.session.sessionId ? result.session : s,
          ),
        );
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to send message');
      } finally {
        setLoading(false);
      }
    },
    [activeSessionId, retrievalFilters],
  );

  const handleShowEvidence = useCallback((citations: CitationDto[]) => {
    setDrawerCitations(citations);
    setDrawerOpen(true);
  }, []);

  const handleSubmitFeedback = useCallback(
    async (messageId: string, request: SubmitFeedbackRequest) => {
      if (!activeSessionId) return;
      try {
        const result = await api.submitFeedback(activeSessionId, messageId, request);
        setFeedbackMap((prev) => {
          const next = new Map(prev);
          next.set(messageId, { type: result.type, reasonCodes: result.reasonCodes });
          return next;
        });
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to submit feedback');
      }
    },
    [activeSessionId],
  );

  const handleRecordOutcome = useCallback(
    async (_sessionId: string, request: RecordOutcomeRequest) => {
      if (!activeSessionId) return;
      try {
        const result = await api.recordOutcome(activeSessionId, request);
        setOutcomeRecorded({ resolutionType: result.resolutionType });
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to record outcome');
      }
    },
    [activeSessionId],
  );

  const handleCreateEscalationDraft = useCallback(
    (messageId: string) => {
      const meta = metaMap.get(messageId);
      if (!meta?.escalation?.recommended) return;
      const msg = messages.find((m) => m.messageId === messageId);
      setDraftMessageId(messageId);
      setDraftEscalation(meta.escalation);
      setDraftCitations(msg?.citations ?? []);
      setDraftModalOpen(true);
    },
    [metaMap, messages],
  );

  return (
    <div className="chat-layout">
      <SessionSidebar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelect={handleSelectSession}
        onNew={handleNewSession}
        onDelete={handleDeleteSession}
      />
      <main className="chat-main">
        <header className="chat-header">
          <h1>Smart KB</h1>
          {activeSessionId && (
            <span className="session-indicator">
              {sessions.find((s) => s.sessionId === activeSessionId)?.title ||
                'New session'}
            </span>
          )}
          <Link to="/patterns" className="nav-admin-link" data-testid="patterns-link">
            Patterns
          </Link>
          <Link to="/admin" className="nav-admin-link" data-testid="admin-link">
            Admin
          </Link>
        </header>
        {error && (
          <div className="error-banner" role="alert" data-testid="error-banner">
            {error}
          </div>
        )}
        {!activeSessionId ? (
          <div className="no-session">
            <p>Select or create a session to start chatting.</p>
            <button onClick={handleNewSession} className="btn btn-primary" aria-label="Start new chat session">
              Start new session
            </button>
          </div>
        ) : (
          <>
            <ChatThread
              messages={messages}
              loading={loading}
              onShowEvidence={handleShowEvidence}
              onCreateEscalationDraft={handleCreateEscalationDraft}
              onSubmitFeedback={handleSubmitFeedback}
              metaMap={metaMap}
              feedbackMap={feedbackMap}
            />
            <div ref={threadEndRef} />
            <RetrievalFilterPanel
              filters={retrievalFilters}
              onChange={setRetrievalFilters}
            />
            <MessageInput onSend={handleSend} disabled={loading} />
            {messages.length > 0 && (
              <OutcomeWidget
                sessionId={activeSessionId}
                existingOutcome={outcomeRecorded}
                onSubmit={handleRecordOutcome}
              />
            )}
          </>
        )}
      </main>
      <EvidenceDrawer
        citations={drawerCitations}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
      />
      {draftModalOpen && activeSessionId && draftMessageId && draftEscalation && (
        <EscalationDraftModal
          open={draftModalOpen}
          sessionId={activeSessionId}
          messageId={draftMessageId}
          escalation={draftEscalation}
          citations={draftCitations}
          onClose={() => setDraftModalOpen(false)}
        />
      )}
    </div>
  );
}
