import { useState, useEffect } from 'react';
import type { PatternDetail, PatternUsageMetrics, PatternVersionHistoryEntry } from '../api/types';
import { getPatternUsage, getPatternHistory } from '../api/client';
import { formatDateTimeOrDash } from '../utils/dateFormat';
import { trustLevelBadgeClass } from '../utils/cssClassHelpers';

interface PatternDetailViewProps {
  pattern: PatternDetail;
  onBack: () => void;
  onReview: (notes: string) => Promise<void>;
  onApprove: (notes: string) => Promise<void>;
  onDeprecate: (reason: string, supersedingPatternId?: string) => Promise<void>;
  actionLoading: boolean;
}

const formatDate = formatDateTimeOrDash;

export function PatternDetailView({
  pattern,
  onBack,
  onReview,
  onApprove,
  onDeprecate,
  actionLoading,
}: PatternDetailViewProps) {
  const [notes, setNotes] = useState('');
  const [deprecateReason, setDeprecateReason] = useState('');
  const [supersedingId, setSupersedingId] = useState('');
  const [showDeprecateForm, setShowDeprecateForm] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [usage, setUsage] = useState<PatternUsageMetrics | null>(null);
  const [usageLoading, setUsageLoading] = useState(false);
  const [versionHistory, setVersionHistory] = useState<PatternVersionHistoryEntry[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setUsageLoading(true);
    getPatternUsage(pattern.patternId)
      .then((m) => { if (!cancelled) setUsage(m); })
      .catch((err) => { console.warn('[PatternDetailView] Failed to load usage metrics:', err); if (!cancelled) setUsage(null); })
      .finally(() => { if (!cancelled) setUsageLoading(false); });
    setHistoryLoading(true);
    getPatternHistory(pattern.patternId)
      .then((h) => { if (!cancelled) setVersionHistory(h.entries); })
      .catch((err) => { console.warn('[PatternDetailView] Failed to load version history:', err); if (!cancelled) setVersionHistory([]); })
      .finally(() => { if (!cancelled) setHistoryLoading(false); });
    return () => { cancelled = true; };
  }, [pattern.patternId]);

  const canReview = pattern.trustLevel === 'Draft';
  const canApprove = pattern.trustLevel === 'Draft' || pattern.trustLevel === 'Reviewed';
  const canDeprecate = pattern.trustLevel !== 'Deprecated';

  async function handleAction(action: () => Promise<void>) {
    setActionError(null);
    try {
      await action();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Action failed');
    }
  }

  return (
    <div className="pattern-detail" data-testid="pattern-detail">
      <div className="pattern-detail-header">
        <button className="btn btn-sm" onClick={onBack} data-testid="pattern-back" aria-label="Back to pattern list">
          Back
        </button>
        <h2>{pattern.title}</h2>
        <span className={trustLevelBadgeClass(pattern.trustLevel)}>{pattern.trustLevel}</span>
      </div>

      {actionError && (
        <div className="error-banner" role="alert" data-testid="pattern-action-error">
          {actionError}
        </div>
      )}

      {/* Governance actions */}
      <div className="pattern-actions" data-testid="pattern-actions">
        {canReview && (
          <button
            className="btn btn-sm"
            disabled={actionLoading}
            onClick={() => handleAction(() => onReview(notes))}
            data-testid="btn-review"
            aria-label="Mark pattern as reviewed"
          >
            Mark Reviewed
          </button>
        )}
        {canApprove && (
          <button
            className="btn btn-primary btn-sm"
            disabled={actionLoading}
            onClick={() => handleAction(() => onApprove(notes))}
            data-testid="btn-approve"
            aria-label="Approve pattern"
          >
            Approve
          </button>
        )}
        {canDeprecate && !showDeprecateForm && (
          <button
            className="btn btn-danger btn-sm"
            disabled={actionLoading}
            onClick={() => setShowDeprecateForm(true)}
            data-testid="btn-show-deprecate"
            aria-label="Deprecate pattern"
          >
            Deprecate
          </button>
        )}
        {(canReview || canApprove) && (
          <input
            type="text"
            placeholder="Optional notes..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            className="pattern-notes-input"
            data-testid="pattern-notes"
            aria-label="Governance action notes"
          />
        )}
      </div>

      {showDeprecateForm && (
        <div className="deprecate-form" data-testid="deprecate-form">
          <div className="field-row">
            <label>Reason:</label>
            <input
              type="text"
              value={deprecateReason}
              onChange={(e) => setDeprecateReason(e.target.value)}
              placeholder="Why is this pattern being deprecated?"
              data-testid="deprecate-reason"
              aria-label="Deprecation reason"
            />
          </div>
          <div className="field-row">
            <label>Superseding Pattern ID:</label>
            <input
              type="text"
              value={supersedingId}
              onChange={(e) => setSupersedingId(e.target.value)}
              placeholder="Optional: pattern-xxx"
              data-testid="deprecate-superseding"
              aria-label="Superseding pattern ID"
            />
          </div>
          <div className="deprecate-actions">
            <button
              className="btn btn-danger btn-sm"
              disabled={actionLoading}
              onClick={() => handleAction(() =>
                onDeprecate(deprecateReason, supersedingId || undefined)
              )}
              data-testid="btn-confirm-deprecate"
              aria-label="Confirm pattern deprecation"
            >
              Confirm Deprecation
            </button>
            <button
              className="btn btn-sm"
              onClick={() => setShowDeprecateForm(false)}
              data-testid="btn-cancel-deprecate"
              aria-label="Cancel deprecation"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Info grid */}
      <div className="pattern-info-grid">
        <div className="info-item">
          <span className="info-label">Pattern ID</span>
          <span className="info-value" data-testid="pattern-id">{pattern.patternId}</span>
        </div>
        <div className="info-item">
          <span className="info-label">Confidence</span>
          <span className="info-value">{(pattern.confidence * 100).toFixed(0)}%</span>
        </div>
        <div className="info-item">
          <span className="info-label">Version</span>
          <span className="info-value">{pattern.version}</span>
        </div>
        <div className="info-item">
          <span className="info-label">Product Area</span>
          <span className="info-value">{pattern.productArea ?? '—'}</span>
        </div>
        <div className="info-item">
          <span className="info-label">Evidence Count</span>
          <span className="info-value">{pattern.relatedEvidenceIds.length}</span>
        </div>
        <div className="info-item">
          <span className="info-label">Visibility</span>
          <span className="info-value">{pattern.visibility}</span>
        </div>
        <div className="info-item">
          <span className="info-label">Created</span>
          <span className="info-value">{formatDate(pattern.createdAt)}</span>
        </div>
        <div className="info-item">
          <span className="info-label">Updated</span>
          <span className="info-value">{formatDate(pattern.updatedAt)}</span>
        </div>
      </div>

      {/* Usage metrics */}
      <div className="pattern-content-section" data-testid="usage-metrics">
        <h3>Usage Metrics</h3>
        {usageLoading && <p className="muted">Loading usage data...</p>}
        {!usageLoading && usage && (
          <>
            <div className="pattern-info-grid">
              <div className="info-item">
                <span className="info-label">Total Citations</span>
                <span className="info-value" data-testid="usage-total">{usage.totalCitations}</span>
              </div>
              <div className="info-item">
                <span className="info-label">Last 7 Days</span>
                <span className="info-value" data-testid="usage-7d">{usage.citationsLast7Days}</span>
              </div>
              <div className="info-item">
                <span className="info-label">Last 30 Days</span>
                <span className="info-value" data-testid="usage-30d">{usage.citationsLast30Days}</span>
              </div>
              <div className="info-item">
                <span className="info-label">Last 90 Days</span>
                <span className="info-value" data-testid="usage-90d">{usage.citationsLast90Days}</span>
              </div>
              <div className="info-item">
                <span className="info-label">Unique Users</span>
                <span className="info-value" data-testid="usage-users">{usage.uniqueUsers}</span>
              </div>
              <div className="info-item">
                <span className="info-label">Avg Confidence</span>
                <span className="info-value" data-testid="usage-confidence">
                  {usage.totalCitations > 0 ? `${(usage.averageConfidence * 100).toFixed(0)}%` : '—'}
                </span>
              </div>
              <div className="info-item">
                <span className="info-label">Last Cited</span>
                <span className="info-value" data-testid="usage-last-cited">
                  {usage.lastCitedAt ? formatDate(usage.lastCitedAt) : 'Never'}
                </span>
              </div>
              <div className="info-item">
                <span className="info-label">First Cited</span>
                <span className="info-value" data-testid="usage-first-cited">
                  {usage.firstCitedAt ? formatDate(usage.firstCitedAt) : 'Never'}
                </span>
              </div>
            </div>
            {usage.dailyBreakdown.some(d => d.citations > 0) && (
              <div className="usage-daily-breakdown" data-testid="usage-daily">
                <h4>Daily Citations (Last 30 Days)</h4>
                <div className="usage-bar-chart">
                  {usage.dailyBreakdown.map((d) => (
                    <div
                      key={d.date}
                      className="usage-bar"
                      title={`${d.date}: ${d.citations}`}
                      style={{
                        height: `${Math.max(d.citations > 0 ? 4 : 0, (d.citations / Math.max(...usage.dailyBreakdown.map(b => b.citations))) * 48)}px`,
                      }}
                    />
                  ))}
                </div>
              </div>
            )}
          </>
        )}
        {!usageLoading && !usage && (
          <p className="muted" data-testid="usage-unavailable">Usage data unavailable.</p>
        )}
      </div>

      {/* Governance history */}
      {(pattern.reviewedBy || pattern.approvedBy || pattern.deprecatedBy) && (
        <div className="pattern-governance-history" data-testid="governance-history">
          <h3>Governance History</h3>
          {pattern.reviewedBy && (
            <div className="governance-event">
              <span className="trust-badge trust-reviewed">Reviewed</span>
              <span>by {pattern.reviewedBy} on {formatDate(pattern.reviewedAt)}</span>
              {pattern.reviewNotes && <span className="governance-notes">{pattern.reviewNotes}</span>}
            </div>
          )}
          {pattern.approvedBy && (
            <div className="governance-event">
              <span className="trust-badge trust-approved">Approved</span>
              <span>by {pattern.approvedBy} on {formatDate(pattern.approvedAt)}</span>
              {pattern.approvalNotes && <span className="governance-notes">{pattern.approvalNotes}</span>}
            </div>
          )}
          {pattern.deprecatedBy && (
            <div className="governance-event">
              <span className="trust-badge trust-deprecated">Deprecated</span>
              <span>by {pattern.deprecatedBy} on {formatDate(pattern.deprecatedAt)}</span>
              {pattern.deprecationReason && <span className="governance-notes">{pattern.deprecationReason}</span>}
            </div>
          )}
        </div>
      )}

      {/* Version history (P3-013) */}
      <div className="pattern-content-section" data-testid="version-history">
        <h3>Version History</h3>
        {historyLoading && <p className="muted">Loading history...</p>}
        {!historyLoading && versionHistory.length === 0 && (
          <p className="muted" data-testid="no-history">No version history recorded.</p>
        )}
        {!historyLoading && versionHistory.length > 0 && (
          <table className="version-history-table" data-testid="history-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Change</th>
                <th>By</th>
                <th>Changed Fields</th>
              </tr>
            </thead>
            <tbody>
              {versionHistory.map((entry) => (
                <tr key={entry.id} data-testid="history-entry">
                  <td>{formatDate(entry.changedAt)}</td>
                  <td>{entry.summary ?? entry.changeType}</td>
                  <td>{entry.changedBy}</td>
                  <td>{entry.changedFields.join(', ')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {pattern.supersedesPatternId && (
        <div className="pattern-supersedes">
          Supersedes: <code>{pattern.supersedesPatternId}</code>
        </div>
      )}

      {/* Problem & resolution content */}
      <div className="pattern-content-section">
        <h3>Problem Statement</h3>
        <p data-testid="problem-statement">{pattern.problemStatement}</p>
      </div>

      {pattern.rootCause && (
        <div className="pattern-content-section">
          <h3>Root Cause</h3>
          <p data-testid="root-cause">{pattern.rootCause}</p>
        </div>
      )}

      {pattern.symptoms.length > 0 && (
        <div className="pattern-content-section">
          <h3>Symptoms</h3>
          <ul>{pattern.symptoms.map((s) => <li key={s}>{s}</li>)}</ul>
        </div>
      )}

      {pattern.diagnosisSteps.length > 0 && (
        <div className="pattern-content-section">
          <h3>Diagnosis Steps</h3>
          <ol>{pattern.diagnosisSteps.map((s) => <li key={s}>{s}</li>)}</ol>
        </div>
      )}

      {pattern.resolutionSteps.length > 0 && (
        <div className="pattern-content-section">
          <h3>Resolution Steps</h3>
          <ol data-testid="resolution-steps">{pattern.resolutionSteps.map((s) => <li key={s}>{s}</li>)}</ol>
        </div>
      )}

      {pattern.verificationSteps.length > 0 && (
        <div className="pattern-content-section">
          <h3>Verification Steps</h3>
          <ol>{pattern.verificationSteps.map((s) => <li key={s}>{s}</li>)}</ol>
        </div>
      )}

      {pattern.workaround && (
        <div className="pattern-content-section">
          <h3>Workaround</h3>
          <p>{pattern.workaround}</p>
        </div>
      )}

      {pattern.escalationCriteria.length > 0 && (
        <div className="pattern-content-section">
          <h3>Escalation Criteria</h3>
          <ul>{pattern.escalationCriteria.map((c) => <li key={c}>{c}</li>)}</ul>
        </div>
      )}

      {pattern.tags.length > 0 && (
        <div className="pattern-tags">
          {pattern.tags.map((tag) => (
            <span key={tag} className="pattern-tag">{tag}</span>
          ))}
        </div>
      )}
    </div>
  );
}
