import { useCallback, useState } from 'react';
import type { RecordOutcomeRequest, ResolutionType } from '../api/types';

const RESOLUTION_LABELS: Record<ResolutionType, string> = {
  ResolvedWithoutEscalation: 'Resolved without escalation',
  Escalated: 'Escalated',
  Rerouted: 'Rerouted',
};

const ALL_RESOLUTION_TYPES: ResolutionType[] = Object.keys(RESOLUTION_LABELS) as ResolutionType[];

interface OutcomeWidgetProps {
  sessionId: string;
  existingOutcome?: { resolutionType: string } | null;
  onSubmit: (sessionId: string, request: RecordOutcomeRequest) => Promise<void>;
}

export function OutcomeWidget({ sessionId, existingOutcome, onSubmit }: OutcomeWidgetProps) {
  const [resolutionType, setResolutionType] = useState<ResolutionType | null>(
    existingOutcome ? (existingOutcome.resolutionType as ResolutionType) : null,
  );
  const [targetTeam, setTargetTeam] = useState('');
  const [acceptance, setAcceptance] = useState<boolean | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(!!existingOutcome);

  const handleSubmit = useCallback(async () => {
    if (!resolutionType) return;
    setSubmitting(true);
    try {
      const request: RecordOutcomeRequest = {
        resolutionType,
        targetTeam: targetTeam || undefined,
        acceptance,
      };
      await onSubmit(sessionId, request);
      setSubmitted(true);
    } finally {
      setSubmitting(false);
    }
  }, [sessionId, onSubmit, resolutionType, targetTeam, acceptance]);

  if (submitted) {
    return (
      <div className="outcome-widget" data-testid="outcome-widget">
        <span className="outcome-thanks" data-testid="outcome-thanks">Outcome recorded</span>
      </div>
    );
  }

  return (
    <div className="outcome-widget" data-testid="outcome-widget">
      <p className="outcome-label">How was this session resolved?</p>
      <div className="outcome-options" data-testid="outcome-options">
        {ALL_RESOLUTION_TYPES.map((type) => (
          <label key={type} className="outcome-option">
            <input
              type="radio"
              name={`outcome-${sessionId}`}
              checked={resolutionType === type}
              onChange={() => setResolutionType(type)}
              data-testid={`resolution-${type}`}
            />
            {RESOLUTION_LABELS[type]}
          </label>
        ))}
      </div>
      {(resolutionType === 'Escalated' || resolutionType === 'Rerouted') && (
        <input
          className="outcome-target-team"
          data-testid="outcome-target-team"
          type="text"
          placeholder="Target team (optional)"
          aria-label="Target team for escalation or reroute"
          value={targetTeam}
          onChange={(e) => setTargetTeam(e.target.value)}
        />
      )}
      {resolutionType && (
        <div className="outcome-acceptance" data-testid="outcome-acceptance">
          <span>Customer accepted resolution?</span>
          <label>
            <input
              type="radio"
              name={`acceptance-${sessionId}`}
              checked={acceptance === true}
              onChange={() => setAcceptance(true)}
              data-testid="acceptance-yes"
            />
            Yes
          </label>
          <label>
            <input
              type="radio"
              name={`acceptance-${sessionId}`}
              checked={acceptance === false}
              onChange={() => setAcceptance(false)}
              data-testid="acceptance-no"
            />
            No
          </label>
        </div>
      )}
      <button
        className="btn btn-sm btn-primary"
        data-testid="submit-outcome"
        onClick={handleSubmit}
        disabled={submitting || !resolutionType}
        aria-label={submitting ? 'Recording outcome' : 'Record session outcome'}
      >
        {submitting ? 'Recording...' : 'Record outcome'}
      </button>
    </div>
  );
}
