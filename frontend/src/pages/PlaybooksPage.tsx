import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { TeamPlaybookDto, CreateTeamPlaybookRequest, UpdateTeamPlaybookRequest } from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type View = 'list' | 'detail' | 'create';

const KNOWN_HANDOFF_FIELDS = [
  'title', 'customerSummary', 'stepsToReproduce', 'logsIdsRequested',
  'suspectedComponent', 'severity', 'targetTeam', 'reason',
];

export function PlaybooksPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [view, setView] = useState<View>('list');
  const [playbooks, setPlaybooks] = useState<TeamPlaybookDto[]>([]);
  const [selected, setSelected] = useState<TeamPlaybookDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  // Create/edit form state
  const [form, setForm] = useState<CreateTeamPlaybookRequest>({
    teamName: '',
    description: '',
    requiredFields: [],
    checklist: [],
  });
  const [checklistInput, setChecklistInput] = useState('');

  const loadPlaybooks = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.listPlaybooks();
      setPlaybooks(data.playbooks);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load playbooks');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (hasAdminRole(roles)) loadPlaybooks();
  }, [roles, loadPlaybooks]);

  if (rolesLoading) {
    return <div className="admin-loading" data-testid="playbooks-loading"><p>Loading...</p></div>;
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="playbooks-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to manage team playbooks.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  function startCreate() {
    setForm({ teamName: '', description: '', requiredFields: [], checklist: [] });
    setChecklistInput('');
    setView('create');
    setError(null);
    setSuccess(null);
  }

  async function handleCreate() {
    setError(null);
    try {
      const created = await api.createPlaybook(form);
      setPlaybooks((prev) => [...prev, created]);
      setSelected(created);
      setView('detail');
      setSuccess('Playbook created');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create playbook');
    }
  }

  async function handleSelectPlaybook(id: string) {
    setError(null);
    try {
      const pb = await api.getPlaybook(id);
      setSelected(pb);
      setView('detail');
      setEditing(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load playbook');
    }
  }

  async function handleUpdate() {
    if (!selected) return;
    setError(null);
    const req: UpdateTeamPlaybookRequest = {
      description: form.description,
      requiredFields: form.requiredFields,
      checklist: form.checklist,
      contactChannel: form.contactChannel,
      requiresApproval: form.requiresApproval,
      minSeverity: form.minSeverity,
      maxConcurrentEscalations: form.maxConcurrentEscalations,
      fallbackTeam: form.fallbackTeam,
    };
    try {
      const updated = await api.updatePlaybook(selected.id, req);
      setSelected(updated);
      setPlaybooks((prev) => prev.map((p) => (p.id === updated.id ? updated : p)));
      setEditing(false);
      setSuccess('Playbook updated');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update playbook');
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this playbook?')) return;
    setError(null);
    try {
      await api.deletePlaybook(id);
      setPlaybooks((prev) => prev.filter((p) => p.id !== id));
      setSelected(null);
      setView('list');
      setSuccess('Playbook deleted');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete playbook');
    }
  }

  function startEditing() {
    if (!selected) return;
    setForm({
      teamName: selected.teamName,
      description: selected.description,
      requiredFields: [...selected.requiredFields],
      checklist: [...selected.checklist],
      contactChannel: selected.contactChannel ?? undefined,
      requiresApproval: selected.requiresApproval,
      minSeverity: selected.minSeverity ?? undefined,
      maxConcurrentEscalations: selected.maxConcurrentEscalations ?? undefined,
      fallbackTeam: selected.fallbackTeam ?? undefined,
    });
    setChecklistInput('');
    setEditing(true);
  }

  function toggleRequiredField(field: string) {
    const current = form.requiredFields ?? [];
    const newFields = current.includes(field)
      ? current.filter((f) => f !== field)
      : [...current, field];
    setForm({ ...form, requiredFields: newFields });
  }

  function addChecklistItem() {
    if (!checklistInput.trim()) return;
    setForm({ ...form, checklist: [...(form.checklist ?? []), checklistInput.trim()] });
    setChecklistInput('');
  }

  function removeChecklistItem(idx: number) {
    const items = [...(form.checklist ?? [])];
    items.splice(idx, 1);
    setForm({ ...form, checklist: items });
  }

  return (
    <div className="admin-layout" data-testid="playbooks-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Team Playbooks</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/routing" className="btn btn-sm">Routing</Link>
          <Link to="/diagnostics" className="btn btn-sm">Diagnostics</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      {error && <div className="error-banner" role="alert" data-testid="playbooks-error">{error}</div>}
      {success && <div className="success-banner" data-testid="playbooks-success">{success}</div>}

      <main className="admin-main">
        {view === 'list' && (
          <>
            <div className="admin-toolbar">
              <button className="btn btn-primary" onClick={startCreate} data-testid="new-playbook-btn">
                New Playbook
              </button>
            </div>
            {loading ? (
              <p>Loading playbooks...</p>
            ) : (
              <table className="admin-table" data-testid="playbooks-table" aria-label="Team playbooks">
                <thead>
                  <tr>
                    <th>Team</th>
                    <th>Description</th>
                    <th>Required Fields</th>
                    <th>Checklist</th>
                    <th>Approval</th>
                    <th>Active</th>
                  </tr>
                </thead>
                <tbody>
                  {playbooks.map((pb) => (
                    <tr key={pb.id} onClick={() => handleSelectPlaybook(pb.id)} className="clickable-row">
                      <td>{pb.teamName}</td>
                      <td>{pb.description || '-'}</td>
                      <td>{pb.requiredFields.length}</td>
                      <td>{pb.checklist.length} items</td>
                      <td>{pb.requiresApproval ? 'Yes' : 'No'}</td>
                      <td>
                        <span className={`status-badge ${pb.isActive ? 'badge-success' : 'badge-muted'}`}>
                          {pb.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </td>
                    </tr>
                  ))}
                  {playbooks.length === 0 && (
                    <tr><td colSpan={6} className="admin-empty">No playbooks configured.</td></tr>
                  )}
                </tbody>
              </table>
            )}
          </>
        )}

        {view === 'create' && (
          <div data-testid="create-playbook-form">
            <h2>Create Playbook</h2>
            {renderPlaybookForm(true)}
          </div>
        )}

        {view === 'detail' && selected && (
          <div data-testid="playbook-detail">
            <div className="admin-toolbar">
              <button className="btn btn-sm" onClick={() => { setView('list'); setEditing(false); }} aria-label="Back to playbook list">Back</button>
              {!editing && (
                <>
                  <button className="btn btn-sm btn-primary" onClick={startEditing} aria-label="Edit playbook">Edit</button>
                  <button className="btn btn-sm btn-danger-outline" onClick={() => handleDelete(selected.id)} aria-label="Delete playbook">Delete</button>
                </>
              )}
            </div>
            {editing ? (
              renderPlaybookForm(false)
            ) : (
              <div className="playbook-detail-view">
                <h2>{selected.teamName}</h2>
                <div className="admin-info-grid">
                  <div><strong>Description:</strong> {selected.description || '-'}</div>
                  <div><strong>Contact Channel:</strong> {selected.contactChannel || '-'}</div>
                  <div><strong>Requires Approval:</strong> {selected.requiresApproval ? 'Yes' : 'No'}</div>
                  <div><strong>Min Severity:</strong> {selected.minSeverity || 'Any'}</div>
                  <div><strong>Max Concurrent:</strong> {selected.maxConcurrentEscalations ?? 'Unlimited'}</div>
                  <div><strong>Fallback Team:</strong> {selected.fallbackTeam || '-'}</div>
                  <div><strong>Active:</strong> {selected.isActive ? 'Yes' : 'No'}</div>
                </div>
                <h3>Required Fields</h3>
                {selected.requiredFields.length > 0 ? (
                  <ul className="playbook-field-list">
                    {selected.requiredFields.map((f) => <li key={f}>{f}</li>)}
                  </ul>
                ) : (
                  <p className="text-muted">None configured</p>
                )}
                <h3>Checklist</h3>
                {selected.checklist.length > 0 ? (
                  <ol className="playbook-checklist">
                    {selected.checklist.map((item, i) => <li key={i}>{item}</li>)}
                  </ol>
                ) : (
                  <p className="text-muted">No checklist items</p>
                )}
              </div>
            )}
          </div>
        )}
      </main>
    </div>
  );

  function renderPlaybookForm(isCreate: boolean) {
    return (
      <div className="playbook-form">
        {isCreate && (
          <div className="draft-field">
            <label>Team Name</label>
            <input value={form.teamName} onChange={(e) => setForm({ ...form, teamName: e.target.value })} />
          </div>
        )}
        <div className="draft-field">
          <label>Description</label>
          <textarea value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} rows={2} />
        </div>
        <div className="draft-field">
          <label>Contact Channel</label>
          <input value={form.contactChannel ?? ''} onChange={(e) => setForm({ ...form, contactChannel: e.target.value || undefined })} placeholder="e.g., #team-slack-channel" />
        </div>
        <div className="draft-field">
          <label>Min Severity</label>
          <select value={form.minSeverity ?? ''} aria-label="Minimum severity" onChange={(e) => setForm({ ...form, minSeverity: e.target.value || undefined })}>
            <option value="">Any</option>
            <option value="P1">P1</option>
            <option value="P2">P2</option>
            <option value="P3">P3</option>
            <option value="P4">P4</option>
          </select>
        </div>
        <div className="draft-field">
          <label>Max Concurrent Escalations</label>
          <input type="number" min={1} value={form.maxConcurrentEscalations ?? ''}
            onChange={(e) => setForm({ ...form, maxConcurrentEscalations: e.target.value ? parseInt(e.target.value) : undefined })} />
        </div>
        <div className="draft-field">
          <label>Fallback Team</label>
          <input value={form.fallbackTeam ?? ''} onChange={(e) => setForm({ ...form, fallbackTeam: e.target.value || undefined })} />
        </div>
        <div className="draft-field">
          <label>
            <input type="checkbox" checked={form.requiresApproval ?? false}
              onChange={(e) => setForm({ ...form, requiresApproval: e.target.checked })} />
            {' '}Requires Approval
          </label>
        </div>

        <div className="draft-field">
          <label>Required Handoff Fields</label>
          <div className="field-chips">
            {KNOWN_HANDOFF_FIELDS.map((field) => (
              <button key={field} type="button"
                className={`filter-chip ${(form.requiredFields ?? []).includes(field) ? 'active' : ''}`}
                onClick={() => toggleRequiredField(field)}>
                {field}
              </button>
            ))}
          </div>
        </div>

        <div className="draft-field">
          <label>Checklist</label>
          <div className="admin-form-row">
            <input value={checklistInput} onChange={(e) => setChecklistInput(e.target.value)}
              placeholder="Add checklist item" aria-label="New checklist item" onKeyDown={(e) => e.key === 'Enter' && addChecklistItem()} />
            <button className="btn btn-sm" type="button" onClick={addChecklistItem}>Add</button>
          </div>
          <ol className="playbook-checklist editable">
            {(form.checklist ?? []).map((item, i) => (
              <li key={i}>
                {item}
                <button className="btn btn-sm btn-close" type="button" aria-label={`Remove checklist item: ${item}`} onClick={() => removeChecklistItem(i)}>&times;</button>
              </li>
            ))}
          </ol>
        </div>

        <div className="admin-form-actions">
          <button className="btn btn-primary" onClick={isCreate ? handleCreate : handleUpdate}>
            {isCreate ? 'Create' : 'Save'}
          </button>
          <button className="btn" onClick={() => { setView(isCreate ? 'list' : 'detail'); setEditing(false); }}>
            Cancel
          </button>
        </div>
      </div>
    );
  }
}
