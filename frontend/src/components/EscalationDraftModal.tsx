import { useCallback, useEffect, useRef, useState } from 'react';
import { logger } from '../utils/logger';
import type {
  CitationDto,
  ConnectorResponse,
  EscalationDraftResponse,
  EscalationSignal,
  ExternalEscalationResult,
} from '../api/types';
import * as api from '../api/client';
import { ConnectorTypes, ConnectorStatuses } from '../constants/enums';

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

  const copyTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // External creation state
  const [connectors, setConnectors] = useState<ConnectorResponse[]>([]);
  const [selectedConnectorId, setSelectedConnectorId] = useState('');
  const [targetProject, setTargetProject] = useState('');
  const [targetListId, setTargetListId] = useState('');
  const [creatingExternal, setCreatingExternal] = useState(false);
  const [externalResult, setExternalResult] = useState<ExternalEscalationResult | null>(null);

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
    return () => {
      if (copyTimerRef.current) clearTimeout(copyTimerRef.current);
    };
  }, []);

  useEffect(() => {
    if (!open) {
      setDraft(null);
      setError(null);
      setCopySuccess(false);
      setExternalResult(null);
      setSelectedConnectorId('');
      setTargetProject('');
      setTargetListId('');
      return;
    }

    let cancelled = false;

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
    (async () => {
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
        if (cancelled) return;
        setDraft(result);
        setTitle(result.title);
        setCustomerSummary(result.customerSummary);
        setStepsToReproduce(result.stepsToReproduce);
        setLogsIdsRequested(result.logsIdsRequested);
        setSuspectedComponent(result.suspectedComponent);
        setSeverity(result.severity);
        setTargetTeam(result.targetTeam);
        setReason(result.reason);

        if (result.externalStatus === 'Created') {
          setExternalResult({
            draftId: result.draftId,
            externalStatus: result.externalStatus,
            externalId: result.externalId ?? null,
            externalUrl: result.externalUrl ?? null,
            errorDetail: null,
            approvedAt: result.approvedAt ?? null,
            connectorType: result.targetConnectorType ?? null,
          });
        }
      } catch (e) {
        logger.warn('[EscalationDraftModal] Failed to create draft:', e);
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to create draft');
      } finally {
        if (!cancelled) setSaving(false);
      }
    })();

    // Load available connectors for external creation
    (async () => {
      try {
        const result = await api.listConnectors();
        if (cancelled) return;
        const escalationConnectors = result.connectors.filter(
          (c) =>
            (c.connectorType === ConnectorTypes.AzureDevOps || c.connectorType === ConnectorTypes.ClickUp) &&
            c.status === ConnectorStatuses.Enabled,
        );
        setConnectors(escalationConnectors);
      } catch (err) {
        logger.warn('[EscalationDraftModal] Failed to load escalation connectors:', err);
        if (!cancelled) setConnectors([]);
      }
    })();

    return () => { cancelled = true; };
  }, [open, sessionId, messageId, escalation, citations]);

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
      logger.warn('[EscalationDraftModal] Failed to save draft:', e);
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
      if (copyTimerRef.current) clearTimeout(copyTimerRef.current);
      copyTimerRef.current = setTimeout(() => setCopySuccess(false), 2000);
    } catch (e) {
      logger.warn('[EscalationDraftModal] Failed to export draft as Markdown:', e);
      setError(e instanceof Error ? e.message : 'Failed to export draft');
    }
  }, [draft]);

  const handleCreateExternal = useCallback(async () => {
    if (!draft || !selectedConnectorId) return;
    setCreatingExternal(true);
    setError(null);
    setExternalResult(null);
    try {
      // Save draft first to capture latest edits
      await api.updateEscalationDraft(draft.draftId, {
        title,
        customerSummary,
        stepsToReproduce,
        logsIdsRequested,
        suspectedComponent,
        severity,
        targetTeam,
        reason,
      });

      const selectedConnector = connectors.find((c) => c.id === selectedConnectorId);
      const result = await api.approveEscalationDraft(draft.draftId, {
        connectorId: selectedConnectorId,
        targetProject: selectedConnector?.connectorType === ConnectorTypes.AzureDevOps ? targetProject || undefined : undefined,
        targetListId: selectedConnector?.connectorType === ConnectorTypes.ClickUp ? targetListId || undefined : undefined,
      });
      setExternalResult(result);

      if (result.externalStatus === 'Created') {
        // Refresh draft to show updated external fields
        const updatedDraft = await api.getEscalationDraft(draft.draftId);
        setDraft(updatedDraft);
      }
    } catch (e) {
      logger.warn('[EscalationDraftModal] Failed to create external work item:', e);
      setError(e instanceof Error ? e.message : 'Failed to create external work item');
    } finally {
      setCreatingExternal(false);
    }
  }, [draft, selectedConnectorId, connectors, targetProject, targetListId, title, customerSummary, stepsToReproduce, logsIdsRequested, suspectedComponent, severity, targetTeam, reason]);

  const selectedConnector = connectors.find((c) => c.id === selectedConnectorId);
  const alreadyCreated = draft?.externalStatus === 'Created' || externalResult?.externalStatus === 'Created';

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
            aria-label="Close escalation draft modal"
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
                  aria-label="Escalation draft title"
                />
              </label>

              <div className="draft-field-row">
                <label className="draft-field draft-field-half">
                  <span className="draft-field-label">Severity</span>
                  <select
                    value={severity}
                    onChange={(e) => setSeverity(e.target.value)}
                    data-testid="draft-severity"
                    aria-label="Escalation severity"
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
                    aria-label="Escalation target team"
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
                  aria-label="Escalation reason"
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
                  aria-label="Customer summary"
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
                  aria-label="Suspected component"
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
                  aria-label="Steps to reproduce"
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
                  aria-label="Logs and IDs requested"
                />
              </label>

              {citations.length > 0 && (
                <div className="draft-evidence-summary" data-testid="draft-evidence-count">
                  {citations.length} evidence link{citations.length !== 1 ? 's' : ''} attached
                </div>
              )}
            </div>

            {/* External creation result banner */}
            {externalResult?.externalStatus === 'Created' && externalResult.externalUrl && (
              <div className="external-creation-success" data-testid="external-creation-success">
                External {externalResult.connectorType === ConnectorTypes.AzureDevOps ? 'work item' : 'task'} created: {' '}
                <a href={externalResult.externalUrl} target="_blank" rel="noopener noreferrer" aria-label={`Open external ${externalResult.connectorType === ConnectorTypes.AzureDevOps ? 'work item' : 'task'} ${externalResult.externalId} (opens in new tab)`}>
                  {externalResult.externalId}
                </a>
              </div>
            )}

            {externalResult?.externalStatus === 'Failed' && (
              <div className="external-creation-error" data-testid="external-creation-error">
                Failed to create external item: {externalResult.errorDetail}
              </div>
            )}

            {/* External creation controls */}
            {connectors.length > 0 && !alreadyCreated && (
              <div className="external-creation-section" data-testid="external-creation-section">
                <label className="draft-field">
                  <span className="draft-field-label">Create in external system</span>
                  <select
                    value={selectedConnectorId}
                    onChange={(e) => setSelectedConnectorId(e.target.value)}
                    data-testid="connector-selector"
                    aria-label="Select connector for external work item creation"
                  >
                    <option value="">Select a connector...</option>
                    {connectors.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name} ({c.connectorType})
                      </option>
                    ))}
                  </select>
                </label>

                {selectedConnector?.connectorType === ConnectorTypes.AzureDevOps && (
                  <label className="draft-field">
                    <span className="draft-field-label">Target Project (optional)</span>
                    <input
                      type="text"
                      value={targetProject}
                      onChange={(e) => setTargetProject(e.target.value)}
                      data-testid="target-project"
                      aria-label="Target project for Azure DevOps work item (optional)"
                      placeholder="Falls back to first configured project"
                    />
                  </label>
                )}

                {selectedConnector?.connectorType === ConnectorTypes.ClickUp && (
                  <label className="draft-field">
                    <span className="draft-field-label">Target List ID (optional)</span>
                    <input
                      type="text"
                      value={targetListId}
                      onChange={(e) => setTargetListId(e.target.value)}
                      data-testid="target-list-id"
                      aria-label="Target list ID for ClickUp task (optional)"
                      placeholder="Falls back to first configured list"
                    />
                  </label>
                )}
              </div>
            )}

            <div className="escalation-draft-actions">
              <button
                className="btn btn-primary"
                onClick={handleSave}
                disabled={saving}
                data-testid="draft-save"
                aria-label={saving ? 'Saving escalation draft' : 'Save escalation draft'}
              >
                {saving ? 'Saving...' : 'Save draft'}
              </button>
              <button
                className="btn"
                onClick={handleCopyMarkdown}
                data-testid="draft-copy-markdown"
                aria-label="Copy escalation draft as Markdown"
              >
                {copySuccess ? 'Copied!' : 'Copy as Markdown'}
              </button>
              {connectors.length > 0 ? (
                <button
                  className="btn btn-escalate"
                  onClick={handleCreateExternal}
                  disabled={!selectedConnectorId || creatingExternal || alreadyCreated}
                  data-testid="draft-create-external"
                  aria-label="Create external work item from escalation draft"
                >
                  {creatingExternal
                    ? 'Creating...'
                    : alreadyCreated
                      ? 'Already created'
                      : selectedConnector?.connectorType === ConnectorTypes.ClickUp
                        ? 'Create ClickUp task'
                        : selectedConnector?.connectorType === ConnectorTypes.AzureDevOps
                          ? 'Create ADO work item'
                          : 'Create external item'}
                </button>
              ) : (
                <>
                  <button
                    className="btn btn-coming-soon"
                    disabled
                    title="No ADO or ClickUp connectors configured"
                    aria-label="Create ADO work item (no connectors configured)"
                    data-testid="draft-create-ado"
                  >
                    Create ADO work item
                  </button>
                  <button
                    className="btn btn-coming-soon"
                    disabled
                    title="No ADO or ClickUp connectors configured"
                    aria-label="Create ClickUp task (no connectors configured)"
                    data-testid="draft-create-clickup"
                  >
                    Create ClickUp task
                  </button>
                </>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
