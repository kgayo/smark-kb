import { useCallback, useState } from 'react';
import type { FeedbackReasonCode, FeedbackType, SubmitFeedbackRequest } from '../api/types';

const REASON_CODE_LABELS: Record<FeedbackReasonCode, string> = {
  WrongAnswer: 'Wrong answer',
  OutdatedInfo: 'Outdated info',
  MissingContext: 'Missing context',
  WrongSource: 'Wrong source',
  TooVague: 'Too vague',
  WrongEscalation: 'Wrong escalation',
  Other: 'Other',
};

const ALL_REASON_CODES: FeedbackReasonCode[] = Object.keys(REASON_CODE_LABELS) as FeedbackReasonCode[];

interface FeedbackWidgetProps {
  messageId: string;
  existingFeedback?: { type: FeedbackType; reasonCodes: string[] } | null;
  onSubmit: (messageId: string, request: SubmitFeedbackRequest) => Promise<void>;
}

export function FeedbackWidget({ messageId, existingFeedback, onSubmit }: FeedbackWidgetProps) {
  const [feedbackType, setFeedbackType] = useState<FeedbackType | null>(
    existingFeedback ? existingFeedback.type : null,
  );
  const [selectedReasons, setSelectedReasons] = useState<Set<FeedbackReasonCode>>(
    () => new Set((existingFeedback?.reasonCodes ?? []) as FeedbackReasonCode[]),
  );
  const [comment, setComment] = useState('');
  const [correctedAnswer, setCorrectedAnswer] = useState('');
  const [showDetails, setShowDetails] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(!!existingFeedback);
  const [error, setError] = useState<string | null>(null);

  const handleThumbsClick = useCallback(
    async (type: FeedbackType) => {
      setFeedbackType(type);

      if (type === 'ThumbsUp') {
        // Thumbs up submits immediately with no reason codes.
        setSubmitting(true);
        setError(null);
        try {
          await onSubmit(messageId, { type: 'ThumbsUp', reasonCodes: [] });
          setSubmitted(true);
          setShowDetails(false);
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Failed to submit feedback');
        } finally {
          setSubmitting(false);
        }
      } else {
        // Thumbs down opens the detail form.
        setShowDetails(true);
        setSubmitted(false);
      }
    },
    [messageId, onSubmit],
  );

  const handleReasonToggle = useCallback((code: FeedbackReasonCode) => {
    setSelectedReasons((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });
  }, []);

  const handleSubmitDetails = useCallback(async () => {
    setSubmitting(true);
    setError(null);
    try {
      const request: SubmitFeedbackRequest = {
        type: 'ThumbsDown',
        reasonCodes: Array.from(selectedReasons),
        comment: comment || undefined,
        correctedAnswer: correctedAnswer || undefined,
      };
      await onSubmit(messageId, request);
      setSubmitted(true);
      setShowDetails(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to submit feedback');
    } finally {
      setSubmitting(false);
    }
  }, [messageId, onSubmit, selectedReasons, comment, correctedAnswer]);

  return (
    <div className="feedback-widget" data-testid="feedback-widget">
      <div className="feedback-thumbs">
        <button
          className={`btn-thumb ${feedbackType === 'ThumbsUp' ? 'active' : ''}`}
          data-testid="thumbs-up"
          onClick={() => handleThumbsClick('ThumbsUp')}
          disabled={submitting}
          aria-label="Helpful"
          title="Helpful"
        >
          <span aria-hidden="true">{'\u{1F44D}'}</span>
        </button>
        <button
          className={`btn-thumb ${feedbackType === 'ThumbsDown' ? 'active' : ''}`}
          data-testid="thumbs-down"
          onClick={() => handleThumbsClick('ThumbsDown')}
          disabled={submitting}
          aria-label="Not helpful"
          title="Not helpful"
        >
          <span aria-hidden="true">{'\u{1F44E}'}</span>
        </button>
        {submitted && <span className="feedback-thanks" data-testid="feedback-thanks">Thanks for your feedback</span>}
        {error && <span className="feedback-error" data-testid="feedback-error">{error}</span>}
      </div>

      {showDetails && feedbackType === 'ThumbsDown' && (
        <div className="feedback-details" data-testid="feedback-details">
          <p className="feedback-details-label">What went wrong?</p>
          <div className="feedback-reason-codes" data-testid="reason-codes">
            {ALL_REASON_CODES.map((code) => (
              <label key={code} className="feedback-reason-option">
                <input
                  type="checkbox"
                  checked={selectedReasons.has(code)}
                  onChange={() => handleReasonToggle(code)}
                  data-testid={`reason-${code}`}
                />
                {REASON_CODE_LABELS[code]}
              </label>
            ))}
          </div>
          <textarea
            className="feedback-comment"
            data-testid="feedback-comment"
            placeholder="Additional comments (optional)"
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={2}
          />
          <textarea
            className="feedback-correction"
            data-testid="feedback-correction"
            placeholder="Suggest a better answer (optional)"
            value={correctedAnswer}
            onChange={(e) => setCorrectedAnswer(e.target.value)}
            rows={2}
          />
          <button
            className="btn btn-sm btn-primary"
            data-testid="submit-feedback"
            onClick={handleSubmitDetails}
            disabled={submitting}
          >
            {submitting ? 'Submitting...' : 'Submit feedback'}
          </button>
        </div>
      )}
    </div>
  );
}
