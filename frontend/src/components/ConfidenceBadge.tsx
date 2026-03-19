import type { ConfidenceLevel } from '../api/types';

interface ConfidenceBadgeProps {
  confidence: number;
  label: ConfidenceLevel;
  rationale?: string | null;
}

const levelClass: Record<ConfidenceLevel, string> = {
  High: 'confidence-high',
  Medium: 'confidence-medium',
  Low: 'confidence-low',
};

export function ConfidenceBadge({ confidence, label, rationale }: ConfidenceBadgeProps) {
  const pct = Math.round(confidence * 100);
  const tooltipText = rationale
    ? `Confidence: ${pct}% (${label}) — ${rationale}`
    : `Confidence: ${pct}% (${label})`;
  return (
    <span className="confidence-badge-wrapper">
      <span
        className={`confidence-badge ${levelClass[label]}`}
        title={tooltipText}
        data-testid="confidence-badge"
      >
        {label} ({pct}%)
      </span>
      {rationale && (
        <span className="confidence-rationale" data-testid="confidence-rationale">
          {rationale}
        </span>
      )}
    </span>
  );
}
