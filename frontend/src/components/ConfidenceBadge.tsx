import type { ConfidenceLevel } from '../api/types';

interface ConfidenceBadgeProps {
  confidence: number;
  label: ConfidenceLevel;
}

const levelClass: Record<ConfidenceLevel, string> = {
  High: 'confidence-high',
  Medium: 'confidence-medium',
  Low: 'confidence-low',
};

export function ConfidenceBadge({ confidence, label }: ConfidenceBadgeProps) {
  const pct = Math.round(confidence * 100);
  return (
    <span
      className={`confidence-badge ${levelClass[label]}`}
      title={`Confidence: ${pct}% (${label})`}
      data-testid="confidence-badge"
    >
      {label} ({pct}%)
    </span>
  );
}
