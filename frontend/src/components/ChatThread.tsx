import type { MessageResponse, CitationDto, ConfidenceLevel } from '../api/types';
import { ConfidenceBadge } from './ConfidenceBadge';

export interface AssistantMeta {
  nextSteps: string[];
  escalation: { recommended: boolean; targetTeam: string; reason: string; handoffNote: string } | null;
}

interface ChatThreadProps {
  messages: MessageResponse[];
  loading: boolean;
  onShowEvidence: (citations: CitationDto[]) => void;
  onCreateEscalationDraft?: (messageId: string) => void;
  metaMap: Map<string, AssistantMeta>;
}

function CitationInline({
  citations,
  onShowEvidence,
}: {
  citations: CitationDto[];
  onShowEvidence: (c: CitationDto[]) => void;
}) {
  if (citations.length === 0) return null;
  return (
    <button
      className="citation-count-btn"
      onClick={() => onShowEvidence(citations)}
      data-testid="show-citations"
    >
      {citations.length} source{citations.length !== 1 ? 's' : ''}
    </button>
  );
}

function NextStepsList({ steps }: { steps: string[] }) {
  if (steps.length === 0) return null;
  return (
    <div className="next-steps" data-testid="next-steps">
      <h4>Suggested next steps:</h4>
      <ul>
        {steps.map((s, i) => (
          <li key={i}>{s}</li>
        ))}
      </ul>
    </div>
  );
}

function EscalationBanner({
  targetTeam,
  reason,
  onCreateDraft,
}: {
  targetTeam: string;
  reason: string;
  onCreateDraft?: () => void;
}) {
  return (
    <div className="escalation-banner" data-testid="escalation-banner">
      <div className="escalation-banner-content">
        <strong>Escalation recommended</strong>
        {targetTeam && <span> to {targetTeam}</span>}
        {reason && <p className="escalation-reason">{reason}</p>}
      </div>
      {onCreateDraft && (
        <button
          className="btn btn-sm btn-escalate"
          data-testid="create-escalation-draft"
          onClick={onCreateDraft}
        >
          Create escalation draft
        </button>
      )}
    </div>
  );
}

export function ChatThread({ messages, loading, onShowEvidence, onCreateEscalationDraft, metaMap }: ChatThreadProps) {
  return (
    <div className="chat-thread" data-testid="chat-thread" role="log" aria-live="polite">
      {messages.length === 0 && !loading && (
        <div className="empty-thread">
          <p>Ask a question to get started.</p>
        </div>
      )}
      {messages.map((msg) => {
        const isAssistant = msg.role === 'assistant';
        const meta = metaMap.get(msg.messageId);
        return (
          <div
            key={msg.messageId}
            className={`message message-${msg.role}`}
            data-testid={`message-${msg.role}`}
          >
            <div className="message-header">
              <span className="message-role">
                {isAssistant ? 'Smart KB' : 'You'}
              </span>
              {isAssistant && msg.confidence != null && msg.confidenceLabel && (
                <ConfidenceBadge
                  confidence={msg.confidence}
                  label={msg.confidenceLabel as ConfidenceLevel}
                />
              )}
            </div>
            <div className="message-body">{msg.content}</div>
            {isAssistant && (
              <div className="message-footer">
                {msg.citations && msg.citations.length > 0 && (
                  <CitationInline
                    citations={msg.citations}
                    onShowEvidence={onShowEvidence}
                  />
                )}
                {meta?.nextSteps && meta.nextSteps.length > 0 && (
                  <NextStepsList steps={meta.nextSteps} />
                )}
                {meta?.escalation?.recommended && (
                  <EscalationBanner
                    targetTeam={meta.escalation.targetTeam}
                    reason={meta.escalation.reason}
                    onCreateDraft={
                      onCreateEscalationDraft
                        ? () => onCreateEscalationDraft(msg.messageId)
                        : undefined
                    }
                  />
                )}
              </div>
            )}
          </div>
        );
      })}
      {loading && (
        <div className="message message-assistant loading" data-testid="typing-indicator">
          <div className="message-header">
            <span className="message-role">Smart KB</span>
          </div>
          <div className="message-body typing-dots">
            <span />
            <span />
            <span />
          </div>
        </div>
      )}
    </div>
  );
}
