import { useCallback, useEffect, useState } from 'react';
import type {
  CitationDto,
  EscalationDraftResponse,
  EscalationSignal,
} from '../api/types';
import * as api from '../api/client';

interface EscalationDraftModalProps {
  open: boolean;
  sessionId: string;
  messageId: string;
  escalation: EscalationSignal;
  citations: CitationDto[];
  onClose: () => void;
}

const SEVERITY_OPTIONS = ['P1', 'P2', 'P3', 'P4'];

export function EscalationDraftModal({
  open,
  sessionId,
  messageId,
  escalation,
  citations,
  onClose,
}: EscalationDraftModalProps) {
  const [draft, setDraft] = useState<EscalationDraftResponse | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copySuccess, setCopySuccess] = useState(false);

  // Form fields
  const [title, setTitle] = useState('');
  const [customerSummary, setCustomerSummary] = useState('');
  const [stepsToReproduce, setStepsToReproduce] = useState('');
  const [logsIdsRequested, setLogsIdsRequested] = useState('');
  const [suspectedComponent, setSuspectedComponent] = useState('');
  const [severity, setSeverity] = useState('P3');
  const [targetTeam, setTargetTeam] = useState('');
  const [reason, setReason] = useState('');

  useEffect(() => {
    if (!open) {
      setDraft(null);
      setError(null);
      setCopySuccess(false);
      return;
    }

    // Pre-fill from escalation signal
    setTargetTeam(escalation.targetTeam || '');
    setReason(escalation.reason || '');
    setTitle(`Escalation: ${escalation.reason || 'Low confidence response'}`);
    setCustomerSummary('');
    setStepsToReproduce('');
    setLogsIdsRequested('');
    setSuspectedComponent('');
    setSeverity('P3');

    // Auto-create draft
    createDraft();
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  async function createDraft() {
    setSaving(true);
    setError(null);
    try {
      const result = await api.createEscalationDraft({
        sessionId,
        messageId,
        title: `Escalation: ${escalation.reason || 'Low confidence response'}`,
        customerSummary: '',
        stepsToReproduce: '',
        logsIdsRequested: '',
        suspectedComponent: '',
        severity: 'P3',
        evidenceLinks: citations,
        targetTeam: escalation.targetTeam || '',
        reason: escalation.reason || '',
      });
      setDraft(result);
      // Sync form fields from server response (routing may have resolved targetTeam)
      setTitle(result.title);
      setCustomerSummary(result.customerSummary);
      setStepsToReproduce(result.stepsToReproduce);
      setLogsIdsRequested(result.logsIdsRequested);
      setSuspectedComponent(result.suspectedComponent);
      setSeverity(result.severity);
      setTargetTeam(result.targetTeam);
      setReason(result.reason);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create draft');
    } finally {
      setSaving(false);
    }
  }

  const handleSave = useCallback(async () => {
    if (!draft) return;
    setSaving(true);
    setError(null);
    try {
      const result = await api.updateEscalationDraft(draft.draftId, {
        title,
        customerSummary,
        stepsToReproduce,
        logsIdsRequested,
        suspectedComponent,
        severity,
        targetTeam,
        reason,
      });
      setDraft(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save draft');
    } finally {
      setSaving(false);
    }
  }, [draft, title, customerSummary, stepsToReproduce, logsIdsRequested, suspectedComponent, severity, targetTeam, reason]);

  const handleCopyMarkdown = useCallback(async () => {
    if (!draft) return;
    setError(null);
    try {
      const result = await api.exportEscalationDraft(draft.draftId);
      await navigator.clipboard.writeText(result.markdown);
      setCopySuccess(true);
      setTimeout(() => setCopySuccess(false), 2000);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to export draft');
    }
  }, [draft]);

  if (!open) return null;

  return (
    <div className="modal-backdrop" data-testid="escalation-draft-modal">
      <div className="escalation-draft-modal">
        <header className="escalation-draft-header">
          <h2>Escalation Draft</h2>
          <button
            className="btn-close"
            onClick={onClose}
            data-testid="escalation-draft-close"
            aria-label="Close"
          >
            &times;
          </button>
        </header>

        {error && (
          <div className="error-banner" role="alert" data-testid="escalation-draft-error">
            {error}
          </div>
        )}

        {saving && !draft && (
          <div className="escalation-draft-loading" data-testid="escalation-draft-loading">
            Creating draft...
          </div>
        )}

        {draft && (
          <div className="escalation-draft-body">
            <div className="escalation-draft-form">
              <label className="draft-field">
                <span className="draft-field-label">Title</span>
                <input
                  type="text"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  data-testid="draft-title"
                />
              </label>

              <div className="draft-field-row">
                <label className="draft-field draft-field-half">
                  <span className="draft-field-label">Severity</span>
                  <select
                    value={severity}
                    onChange={(e) => setSeverity(e.target.value)}
                    data-testid="draft-severity"
                  >
                    {SEVERITY_OPTIONS.map((s) => (
                      <option key={s} value={s}>{s}</option>
                    ))}
                  </select>
                </label>

                <label className="draft-field draft-field-half">
                  <span className="draft-field-label">Target Team</span>
                  <input
                    type="text"
                    value={targetTeam}
                    onChange={(e) => setTargetTeam(e.target.value)}
                    data-testid="draft-target-team"
                  />
                </label>
              </div>

              <label className="draft-field">
                <span className="draft-field-label">Reason</span>
                <input
                  type="text"
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  data-testid="draft-reason"
                />
              </label>

              <label className="draft-field">
                <span className="draft-field-label">Customer Summary</span>
                <textarea
                  rows={3}
                  value={customerSummary}
                  onChange={(e) => setCustomerSummary(e.target.value)}
                  data-testid="draft-customer-summary"
                  placeholder="Describe the customer's issue..."
                />
              </label>

              <label className="draft-field">
                <span className="draft-field-label">Suspected Component</span>
                <input
                  type="text"
                  value={suspectedComponent}
                  onChange={(e) => setSuspectedComponent(e.target.value)}
                  data-testid="draft-suspected-component"
                  placeholder="e.g., Authentication, Billing"
                />
              </label>

              <label className="draft-field">
                <span className="draft-field-label">Steps to Reproduce</span>
                <textarea
                  rows={3}
                  value={stepsToReproduce}
                  onChange={(e) => setStepsToReproduce(e.target.value)}
                  data-testid="draft-steps-to-reproduce"
                  placeholder="1. Go to...&#10;2. Click on..."
                />
              </label>

              <label className="draft-field">
                <span className="draft-field-label">Logs / IDs Requested</span>
                <textarea
                  rows={2}
                  value={logsIdsRequested}
                  onChange={(e) => setLogsIdsRequested(e.target.value)}
                  data-testid="draft-logs-ids"
                  placeholder="Correlation IDs, log file references..."
                />
              </label>

              {citations.length > 0 && (
                <div className="draft-evidence-summary" data-testid="draft-evidence-count">
                  {citations.length} evidence link{citations.length !== 1 ? 's' : ''} attached
                </div>
              )}
            </div>

            <div className="escalation-draft-actions">
              <button
                className="btn btn-primary"
                onClick={handleSave}
                disabled={saving}
                data-testid="draft-save"
              >
                {saving ? 'Saving...' : 'Save draft'}
              </button>
              <button
                className="btn"
                onClick={handleCopyMarkdown}
                data-testid="draft-copy-markdown"
              >
                {copySuccess ? 'Copied!' : 'Copy as Markdown'}
              </button>
              <button
                className="btn btn-coming-soon"
                disabled
                title="Coming soon — external ticket creation in Phase 2"
                data-testid="draft-create-ado"
              >
                Create ADO work item
              </button>
              <button
                className="btn btn-coming-soon"
                disabled
                title="Coming soon — external ticket creation in Phase 2"
                data-testid="draft-create-clickup"
              >
                Create ClickUp task
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
