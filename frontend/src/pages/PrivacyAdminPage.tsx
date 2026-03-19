import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  PiiPolicyResponse,
  PiiPolicyUpdateRequest,
  CustomPiiPattern,
  RetentionPolicyEntry,
  RetentionPolicyUpdateRequest,
  RetentionCleanupResult,
  DataSubjectDeletionResponse,
  RetentionComplianceReport,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type Tab = 'pii' | 'retention' | 'deletion' | 'compliance';

const PII_TYPES = ['email', 'phone', 'ssn', 'credit_card'];
const ENFORCEMENT_MODES = ['redact', 'detect', 'disabled'];
const ENTITY_TYPES = ['AppSession', 'Message', 'AuditEvent', 'EvidenceChunk', 'AnswerTrace'];

export function PrivacyAdminPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [tab, setTab] = useState<Tab>('pii');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // PII policy state
  const [piiPolicy, setPiiPolicy] = useState<PiiPolicyResponse | null>(null);
  const [piiLoading, setPiiLoading] = useState(false);
  const [piiEditing, setPiiEditing] = useState(false);
  const [piiForm, setPiiForm] = useState<PiiPolicyUpdateRequest>({
    enforcementMode: 'redact',
    enabledPiiTypes: [],
  });
  const [newPattern, setNewPattern] = useState<CustomPiiPattern>({ name: '', pattern: '', placeholder: '' });

  // Retention state
  const [retentionPolicies, setRetentionPolicies] = useState<RetentionPolicyEntry[]>([]);
  const [retentionLoading, setRetentionLoading] = useState(false);
  const [retentionForm, setRetentionForm] = useState<RetentionPolicyUpdateRequest>({
    entityType: 'AppSession',
    retentionDays: 90,
  });
  const [showRetentionForm, setShowRetentionForm] = useState(false);
  const [cleanupResults, setCleanupResults] = useState<RetentionCleanupResult[] | null>(null);

  // Deletion state
  const [deletionRequests, setDeletionRequests] = useState<DataSubjectDeletionResponse[]>([]);
  const [deletionLoading, setDeletionLoading] = useState(false);
  const [subjectId, setSubjectId] = useState('');

  // Compliance state
  const [compliance, setCompliance] = useState<RetentionComplianceReport | null>(null);
  const [complianceLoading, setComplianceLoading] = useState(false);

  const loadPiiPolicy = useCallback(async () => {
    setPiiLoading(true);
    setError(null);
    try {
      const data = await api.getPiiPolicy();
      setPiiPolicy(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load PII policy');
    } finally {
      setPiiLoading(false);
    }
  }, []);

  const loadRetention = useCallback(async () => {
    setRetentionLoading(true);
    setError(null);
    try {
      const data = await api.getRetentionPolicies();
      setRetentionPolicies(data.policies);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load retention policies');
    } finally {
      setRetentionLoading(false);
    }
  }, []);

  const loadDeletions = useCallback(async () => {
    setDeletionLoading(true);
    setError(null);
    try {
      const data = await api.listDeletionRequests();
      setDeletionRequests(data.requests);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load deletion requests');
    } finally {
      setDeletionLoading(false);
    }
  }, []);

  const loadCompliance = useCallback(async () => {
    setComplianceLoading(true);
    setError(null);
    try {
      const data = await api.getRetentionCompliance();
      setCompliance(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load compliance report');
    } finally {
      setComplianceLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!hasAdminRole(roles)) return;
    if (tab === 'pii') loadPiiPolicy();
    else if (tab === 'retention') loadRetention();
    else if (tab === 'deletion') loadDeletions();
    else if (tab === 'compliance') loadCompliance();
  }, [roles, tab, loadPiiPolicy, loadRetention, loadDeletions, loadCompliance]);

  if (rolesLoading) {
    return <div className="admin-loading" data-testid="privacy-loading"><p>Loading...</p></div>;
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="privacy-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to manage privacy settings.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  function startPiiEdit() {
    if (piiPolicy) {
      setPiiForm({
        enforcementMode: piiPolicy.enforcementMode,
        enabledPiiTypes: [...piiPolicy.enabledPiiTypes],
        customPatterns: piiPolicy.customPatterns ? [...piiPolicy.customPatterns] : [],
        auditRedactions: piiPolicy.auditRedactions,
      });
    } else {
      setPiiForm({ enforcementMode: 'redact', enabledPiiTypes: [...PII_TYPES] });
    }
    setPiiEditing(true);
  }

  async function handleSavePiiPolicy() {
    setError(null);
    try {
      const data = await api.updatePiiPolicy(piiForm);
      setPiiPolicy(data);
      setPiiEditing(false);
      setSuccess('PII policy updated');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save PII policy');
    }
  }

  async function handleResetPiiPolicy() {
    if (!confirm('Reset PII policy to defaults?')) return;
    setError(null);
    try {
      await api.resetPiiPolicy();
      setPiiPolicy(null);
      setSuccess('PII policy reset');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to reset PII policy');
    }
  }

  function togglePiiType(type: string) {
    const current = piiForm.enabledPiiTypes;
    const updated = current.includes(type)
      ? current.filter((t) => t !== type)
      : [...current, type];
    setPiiForm({ ...piiForm, enabledPiiTypes: updated });
  }

  function addCustomPattern() {
    if (!newPattern.name || !newPattern.pattern || !newPattern.placeholder) return;
    const patterns = [...(piiForm.customPatterns ?? []), { ...newPattern }];
    setPiiForm({ ...piiForm, customPatterns: patterns });
    setNewPattern({ name: '', pattern: '', placeholder: '' });
  }

  function removeCustomPattern(idx: number) {
    const patterns = [...(piiForm.customPatterns ?? [])];
    patterns.splice(idx, 1);
    setPiiForm({ ...piiForm, customPatterns: patterns });
  }

  async function handleSaveRetention() {
    setError(null);
    try {
      const entry = await api.updateRetentionPolicy(retentionForm);
      setRetentionPolicies((prev) => {
        const exists = prev.findIndex((p) => p.entityType === entry.entityType);
        if (exists >= 0) {
          const updated = [...prev];
          updated[exists] = entry;
          return updated;
        }
        return [...prev, entry];
      });
      setShowRetentionForm(false);
      setSuccess('Retention policy saved');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save retention policy');
    }
  }

  async function handleDeleteRetention(entityType: string) {
    if (!confirm(`Delete retention policy for ${entityType}?`)) return;
    setError(null);
    try {
      await api.deleteRetentionPolicy(entityType);
      setRetentionPolicies((prev) => prev.filter((p) => p.entityType !== entityType));
      setSuccess('Retention policy deleted');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete retention policy');
    }
  }

  async function handleRunCleanup() {
    if (!confirm('Run retention cleanup now? This will delete expired data.')) return;
    setError(null);
    try {
      const results = await api.runRetentionCleanup();
      setCleanupResults(results);
      setSuccess(`Cleanup completed: ${results.reduce((sum, r) => sum + r.deletedCount, 0)} records deleted`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Cleanup failed');
    }
  }

  async function handleCreateDeletion() {
    if (!subjectId.trim()) return;
    setError(null);
    try {
      const result = await api.createDeletionRequest({ subjectId: subjectId.trim() });
      setDeletionRequests((prev) => [result, ...prev]);
      setSubjectId('');
      setSuccess('Deletion request created');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create deletion request');
    }
  }

  return (
    <div className="admin-layout" data-testid="privacy-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Privacy Management</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/routing" className="btn btn-sm">Routing</Link>
          <Link to="/diagnostics" className="btn btn-sm">Diagnostics</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      {error && <div className="error-banner" role="alert" data-testid="privacy-error">{error}</div>}
      {success && <div className="success-banner" data-testid="privacy-success">{success}</div>}

      <div className="admin-tabs">
        <button className={`admin-tab ${tab === 'pii' ? 'active' : ''}`} onClick={() => setTab('pii')}>
          PII Policy
        </button>
        <button className={`admin-tab ${tab === 'retention' ? 'active' : ''}`} onClick={() => setTab('retention')}>
          Retention
        </button>
        <button className={`admin-tab ${tab === 'deletion' ? 'active' : ''}`} onClick={() => setTab('deletion')}>
          Data Deletion
        </button>
        <button className={`admin-tab ${tab === 'compliance' ? 'active' : ''}`} onClick={() => setTab('compliance')}>
          Compliance
        </button>
      </div>

      <main className="admin-main">
        {tab === 'pii' && (
          <div data-testid="pii-panel">
            {piiLoading ? (
              <p>Loading PII policy...</p>
            ) : piiEditing ? (
              <div className="pii-form">
                <h3>Edit PII Policy</h3>
                <div className="draft-field">
                  <label>Enforcement Mode</label>
                  <select value={piiForm.enforcementMode}
                    onChange={(e) => setPiiForm({ ...piiForm, enforcementMode: e.target.value })}>
                    {ENFORCEMENT_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
                  </select>
                </div>
                <div className="draft-field">
                  <label>Enabled PII Types</label>
                  <div className="field-chips">
                    {PII_TYPES.map((type) => (
                      <button key={type} type="button"
                        className={`filter-chip ${piiForm.enabledPiiTypes.includes(type) ? 'active' : ''}`}
                        onClick={() => togglePiiType(type)}>
                        {type}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="draft-field">
                  <label>
                    <input type="checkbox" checked={piiForm.auditRedactions ?? true}
                      onChange={(e) => setPiiForm({ ...piiForm, auditRedactions: e.target.checked })} />
                    {' '}Audit Redactions
                  </label>
                </div>

                <h4>Custom Patterns</h4>
                {(piiForm.customPatterns ?? []).map((p, i) => (
                  <div key={i} className="admin-form-row">
                    <span>{p.name}: <code>{p.pattern}</code> &rarr; {p.placeholder}</span>
                    <button className="btn btn-sm btn-close" onClick={() => removeCustomPattern(i)}>&times;</button>
                  </div>
                ))}
                <div className="admin-form-row">
                  <input placeholder="Name" value={newPattern.name}
                    onChange={(e) => setNewPattern({ ...newPattern, name: e.target.value })} />
                  <input placeholder="Regex pattern" value={newPattern.pattern}
                    onChange={(e) => setNewPattern({ ...newPattern, pattern: e.target.value })} />
                  <input placeholder="Placeholder" value={newPattern.placeholder}
                    onChange={(e) => setNewPattern({ ...newPattern, placeholder: e.target.value })} />
                  <button className="btn btn-sm" onClick={addCustomPattern}>Add</button>
                </div>

                <div className="admin-form-actions">
                  <button className="btn btn-primary" onClick={handleSavePiiPolicy}>Save</button>
                  <button className="btn" onClick={() => setPiiEditing(false)}>Cancel</button>
                </div>
              </div>
            ) : (
              <div>
                <div className="admin-toolbar">
                  <button className="btn btn-sm btn-primary" onClick={startPiiEdit}>
                    {piiPolicy ? 'Edit Policy' : 'Configure Policy'}
                  </button>
                  {piiPolicy && (
                    <button className="btn btn-sm btn-danger-outline" onClick={handleResetPiiPolicy}>Reset</button>
                  )}
                </div>
                {piiPolicy ? (
                  <div className="admin-info-grid">
                    <div><strong>Enforcement Mode:</strong> {piiPolicy.enforcementMode}</div>
                    <div><strong>Enabled PII Types:</strong> {piiPolicy.enabledPiiTypes.join(', ') || 'None'}</div>
                    <div><strong>Audit Redactions:</strong> {piiPolicy.auditRedactions ? 'Yes' : 'No'}</div>
                    <div><strong>Custom Patterns:</strong> {piiPolicy.customPatterns.length}</div>
                    <div><strong>Updated:</strong> {new Date(piiPolicy.updatedAt).toLocaleString()}</div>
                  </div>
                ) : (
                  <p>No PII policy configured. Using system defaults.</p>
                )}
              </div>
            )}
          </div>
        )}

        {tab === 'retention' && (
          <div data-testid="retention-panel">
            <div className="admin-toolbar">
              <button className="btn btn-sm btn-primary" onClick={() => setShowRetentionForm(true)}>
                Add/Update Policy
              </button>
              <button className="btn btn-sm btn-danger-outline" onClick={handleRunCleanup}>
                Run Cleanup Now
              </button>
            </div>
            {showRetentionForm && (
              <div className="admin-form-inline" data-testid="retention-form">
                <div className="admin-form-row">
                  <select value={retentionForm.entityType}
                    onChange={(e) => setRetentionForm({ ...retentionForm, entityType: e.target.value })}>
                    {ENTITY_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
                  </select>
                  <input type="number" min={1} placeholder="Retention days" value={retentionForm.retentionDays}
                    onChange={(e) => setRetentionForm({ ...retentionForm, retentionDays: parseInt(e.target.value) || 1 })} />
                  <input type="number" min={1} placeholder="Metric retention days (optional)"
                    value={retentionForm.metricRetentionDays ?? ''}
                    onChange={(e) => setRetentionForm({ ...retentionForm, metricRetentionDays: e.target.value ? parseInt(e.target.value) : undefined })} />
                  <button className="btn btn-sm btn-primary" onClick={handleSaveRetention}>Save</button>
                  <button className="btn btn-sm" onClick={() => setShowRetentionForm(false)}>Cancel</button>
                </div>
              </div>
            )}
            {retentionLoading ? (
              <p>Loading retention policies...</p>
            ) : (
              <table className="admin-table" data-testid="retention-table">
                <thead>
                  <tr>
                    <th>Entity Type</th>
                    <th>Retention Days</th>
                    <th>Metric Days</th>
                    <th>Updated</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {retentionPolicies.map((p) => (
                    <tr key={p.entityType}>
                      <td>{p.entityType}</td>
                      <td>{p.retentionDays}</td>
                      <td>{p.metricRetentionDays ?? '-'}</td>
                      <td>{new Date(p.updatedAt).toLocaleString()}</td>
                      <td>
                        <button className="btn btn-sm btn-danger-outline"
                          onClick={() => handleDeleteRetention(p.entityType)}>Delete</button>
                      </td>
                    </tr>
                  ))}
                  {retentionPolicies.length === 0 && (
                    <tr><td colSpan={5} className="admin-empty">No retention policies configured.</td></tr>
                  )}
                </tbody>
              </table>
            )}
            {cleanupResults && (
              <div className="cleanup-results" data-testid="cleanup-results">
                <h4>Cleanup Results</h4>
                <table className="admin-table">
                  <thead>
                    <tr><th>Entity Type</th><th>Deleted</th><th>Cutoff Date</th></tr>
                  </thead>
                  <tbody>
                    {cleanupResults.map((r) => (
                      <tr key={r.entityType}>
                        <td>{r.entityType}</td>
                        <td>{r.deletedCount}</td>
                        <td>{new Date(r.cutoffDate).toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {tab === 'deletion' && (
          <div data-testid="deletion-panel">
            <div className="admin-toolbar">
              <input placeholder="Subject/User ID" value={subjectId}
                onChange={(e) => setSubjectId(e.target.value)} />
              <button className="btn btn-sm btn-primary" onClick={handleCreateDeletion}
                disabled={!subjectId.trim()}>
                Submit Deletion Request
              </button>
            </div>
            {deletionLoading ? (
              <p>Loading deletion requests...</p>
            ) : (
              <table className="admin-table" data-testid="deletion-table">
                <thead>
                  <tr>
                    <th>Request ID</th>
                    <th>Subject ID</th>
                    <th>Status</th>
                    <th>Requested</th>
                    <th>Completed</th>
                    <th>Summary</th>
                  </tr>
                </thead>
                <tbody>
                  {deletionRequests.map((req) => (
                    <tr key={req.requestId}>
                      <td className="text-mono">{req.requestId.substring(0, 8)}...</td>
                      <td>{req.subjectId}</td>
                      <td>
                        <span className={`status-badge ${req.status === 'Completed' ? 'badge-success' : req.status === 'Failed' ? 'badge-danger' : 'badge-info'}`}>
                          {req.status}
                        </span>
                      </td>
                      <td>{new Date(req.requestedAt).toLocaleString()}</td>
                      <td>{req.completedAt ? new Date(req.completedAt).toLocaleString() : '-'}</td>
                      <td>
                        {req.deletionSummary
                          ? Object.entries(req.deletionSummary).map(([k, v]) => `${k}: ${v}`).join(', ')
                          : req.errorDetail ?? '-'}
                      </td>
                    </tr>
                  ))}
                  {deletionRequests.length === 0 && (
                    <tr><td colSpan={6} className="admin-empty">No deletion requests.</td></tr>
                  )}
                </tbody>
              </table>
            )}
          </div>
        )}

        {tab === 'compliance' && (
          <div data-testid="compliance-panel">
            <div className="admin-toolbar">
              <button className="btn btn-sm btn-primary" onClick={loadCompliance}>Refresh</button>
            </div>
            {complianceLoading ? (
              <p>Loading compliance report...</p>
            ) : compliance ? (
              <>
                <div className="admin-cards">
                  <div className={`admin-card ${compliance.isCompliant ? '' : 'card-danger'}`}>
                    <div className="admin-card-label">Status</div>
                    <div className="admin-card-value">
                      {compliance.isCompliant ? 'Compliant' : 'Non-Compliant'}
                    </div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Total Policies</div>
                    <div className="admin-card-value">{compliance.totalPolicies}</div>
                  </div>
                  <div className={`admin-card ${compliance.overduePolicies > 0 ? 'card-danger' : ''}`}>
                    <div className="admin-card-label">Overdue</div>
                    <div className="admin-card-value">{compliance.overduePolicies}</div>
                  </div>
                </div>

                <table className="admin-table" data-testid="compliance-table">
                  <thead>
                    <tr>
                      <th>Entity Type</th>
                      <th>Retention Days</th>
                      <th>Last Executed</th>
                      <th>Days Since</th>
                      <th>Last Deleted</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {compliance.entries.map((entry) => (
                      <tr key={entry.entityType}>
                        <td>{entry.entityType}</td>
                        <td>{entry.retentionDays}</td>
                        <td>{entry.lastExecutedAt ? new Date(entry.lastExecutedAt).toLocaleString() : 'Never'}</td>
                        <td>{entry.daysSinceLastExecution}</td>
                        <td>{entry.lastDeletedCount ?? '-'}</td>
                        <td>
                          <span className={`status-badge ${entry.isOverdue ? 'badge-danger' : 'badge-success'}`}>
                            {entry.isOverdue ? 'Overdue' : 'OK'}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </>
            ) : (
              <p>No compliance data available.</p>
            )}
          </div>
        )}
      </main>
    </div>
  );
}
