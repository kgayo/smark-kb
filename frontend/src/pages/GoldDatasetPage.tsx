import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  GoldCaseListResponse,
  GoldCaseDetail,
  CreateGoldCaseRequest,
  GoldCaseExpected,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type Tab = 'cases' | 'create' | 'export';

export function GoldDatasetPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [tab, setTab] = useState<Tab>('cases');

  // ── Cases tab state ──
  const [cases, setCases] = useState<GoldCaseListResponse | null>(null);
  const [casesLoading, setCasesLoading] = useState(false);
  const [casesError, setCasesError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [tagFilter, setTagFilter] = useState('');
  const [selectedCase, setSelectedCase] = useState<GoldCaseDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  // ── Create tab state ──
  const [createForm, setCreateForm] = useState({
    caseId: '',
    query: '',
    responseType: 'final_answer',
    mustInclude: '',
    tags: '',
    mustCiteSources: true,
    shouldHaveEvidence: true,
  });
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createSuccess, setCreateSuccess] = useState<string | null>(null);

  // ── Export tab state ──
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const loadCases = useCallback(async (p: number, tag: string) => {
    setCasesLoading(true);
    setCasesError(null);
    try {
      const result = await api.listGoldCases(tag || undefined, p, 20);
      setCases(result);
    } catch (e) {
      setCasesError(e instanceof Error ? e.message : 'Failed to load gold cases');
    } finally {
      setCasesLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!hasAdminRole(roles) || tab !== 'cases') return;
    loadCases(page, tagFilter);
  }, [roles, tab, page, tagFilter, loadCases]);

  const handleSelectCase = async (id: string) => {
    setDetailLoading(true);
    try {
      const detail = await api.getGoldCase(id);
      setSelectedCase(detail);
    } catch {
      setSelectedCase(null);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleDeleteCase = async (id: string) => {
    if (!confirm('Delete this gold case?')) return;
    try {
      await api.deleteGoldCase(id);
      setSelectedCase(null);
      loadCases(page, tagFilter);
    } catch (e) {
      setCasesError(e instanceof Error ? e.message : 'Delete failed');
    }
  };

  const handleCreate = async () => {
    setCreating(true);
    setCreateError(null);
    setCreateSuccess(null);
    try {
      const expected: GoldCaseExpected = {
        responseType: createForm.responseType,
        mustInclude: createForm.mustInclude
          ? createForm.mustInclude.split(',').map((s) => s.trim()).filter(Boolean)
          : undefined,
        mustCiteSources: createForm.mustCiteSources,
        shouldHaveEvidence: createForm.shouldHaveEvidence,
      };
      const req: CreateGoldCaseRequest = {
        caseId: createForm.caseId,
        query: createForm.query,
        expected,
        tags: createForm.tags
          ? createForm.tags.split(',').map((s) => s.trim()).filter(Boolean)
          : undefined,
      };
      await api.createGoldCase(req);
      setCreateSuccess(`Gold case ${createForm.caseId} created.`);
      setCreateForm({
        caseId: '',
        query: '',
        responseType: 'final_answer',
        mustInclude: '',
        tags: '',
        mustCiteSources: true,
        shouldHaveEvidence: true,
      });
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Create failed');
    } finally {
      setCreating(false);
    }
  };

  const handleExport = async () => {
    setExporting(true);
    setExportError(null);
    try {
      const jsonl = await api.exportGoldCases();
      const blob = new Blob([jsonl], { type: 'application/x-ndjson' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      const ts = new Date().toISOString().replace(/[:.]/g, '-');
      a.download = `gold-dataset-${ts}.jsonl`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (e) {
      setExportError(e instanceof Error ? e.message : 'Export failed');
    } finally {
      setExporting(false);
    }
  };

  if (rolesLoading) {
    return (
      <div className="admin-loading" data-testid="gold-loading">
        <p>Loading...</p>
      </div>
    );
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="gold-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to manage the gold dataset.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  return (
    <div className="admin-layout" data-testid="gold-dataset-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Gold Dataset</h1>
          <span className="admin-subtitle">{cases ? `${cases.totalCount} cases` : ''}</span>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/diagnostics" className="btn btn-sm">Diagnostics</Link>
          <Link to="/audit" className="btn btn-sm">Audit</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      <div className="admin-tabs" data-testid="gold-tabs">
        <button
          className={`admin-tab${tab === 'cases' ? ' active' : ''}`}
          onClick={() => setTab('cases')}
          data-testid="tab-cases"
        >
          Cases
        </button>
        <button
          className={`admin-tab${tab === 'create' ? ' active' : ''}`}
          onClick={() => setTab('create')}
          data-testid="tab-create"
        >
          Create
        </button>
        <button
          className={`admin-tab${tab === 'export' ? ' active' : ''}`}
          onClick={() => setTab('export')}
          data-testid="tab-export"
        >
          Export
        </button>
      </div>

      <main className="admin-main">
        {tab === 'cases' && (
          <CasesPanel
            cases={cases}
            loading={casesLoading}
            error={casesError}
            page={page}
            tagFilter={tagFilter}
            selectedCase={selectedCase}
            detailLoading={detailLoading}
            onPageChange={setPage}
            onTagFilterChange={(v) => { setTagFilter(v); setPage(1); }}
            onSelectCase={handleSelectCase}
            onDeleteCase={handleDeleteCase}
            onCloseDetail={() => setSelectedCase(null)}
          />
        )}
        {tab === 'create' && (
          <CreatePanel
            form={createForm}
            creating={creating}
            error={createError}
            success={createSuccess}
            onChange={(key, value) =>
              setCreateForm((prev) => ({ ...prev, [key]: value }))
            }
            onCreate={handleCreate}
          />
        )}
        {tab === 'export' && (
          <ExportPanel
            exporting={exporting}
            error={exportError}
            onExport={handleExport}
          />
        )}
      </main>
    </div>
  );
}

// ── Cases Panel ──

interface CasesPanelProps {
  cases: GoldCaseListResponse | null;
  loading: boolean;
  error: string | null;
  page: number;
  tagFilter: string;
  selectedCase: GoldCaseDetail | null;
  detailLoading: boolean;
  onPageChange: (p: number) => void;
  onTagFilterChange: (v: string) => void;
  onSelectCase: (id: string) => void;
  onDeleteCase: (id: string) => void;
  onCloseDetail: () => void;
}

function CasesPanel({
  cases,
  loading,
  error,
  page,
  tagFilter,
  selectedCase,
  detailLoading,
  onPageChange,
  onTagFilterChange,
  onSelectCase,
  onDeleteCase,
  onCloseDetail,
}: CasesPanelProps) {
  return (
    <div data-testid="cases-panel">
      <div className="admin-toolbar" data-testid="gold-filters">
        <input
          type="text"
          placeholder="Filter by tag"
          value={tagFilter}
          onChange={(e) => onTagFilterChange(e.target.value)}
          data-testid="filter-tag"
        />
      </div>

      {error && <div className="error-banner" role="alert" data-testid="cases-error">{error}</div>}
      {loading && <p>Loading cases...</p>}

      {cases && !loading && (
        <>
          <table className="admin-table" data-testid="cases-table">
            <thead>
              <tr>
                <th>Case ID</th>
                <th>Query</th>
                <th>Response Type</th>
                <th>Tags</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {cases.cases.map((c) => (
                <tr
                  key={c.id}
                  onClick={() => onSelectCase(c.id)}
                  style={{ cursor: 'pointer' }}
                  data-testid={`case-row-${c.caseId}`}
                >
                  <td><code>{c.caseId}</code></td>
                  <td>{c.query.length > 60 ? c.query.slice(0, 60) + '...' : c.query}</td>
                  <td><span className="status-badge">{c.responseType}</span></td>
                  <td>{c.tags.join(', ')}</td>
                  <td>{new Date(c.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
              {cases.cases.length === 0 && (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center' }}>
                    No gold cases found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div className="audit-pagination" data-testid="cases-pagination">
            <button
              className="btn btn-sm"
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
              data-testid="page-prev"
            >
              Previous
            </button>
            <span>Page {cases.page} of {Math.max(1, Math.ceil(cases.totalCount / cases.pageSize))}</span>
            <button
              className="btn btn-sm"
              disabled={!cases.hasMore}
              onClick={() => onPageChange(page + 1)}
              data-testid="page-next"
            >
              Next
            </button>
          </div>
        </>
      )}

      {detailLoading && <p>Loading detail...</p>}

      {selectedCase && !detailLoading && (
        <div className="admin-detail-panel" data-testid="case-detail">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3>{selectedCase.caseId}</h3>
            <div>
              <button
                className="btn btn-sm btn-danger"
                onClick={() => onDeleteCase(selectedCase.id)}
                data-testid="delete-case-btn"
              >
                Delete
              </button>
              <button
                className="btn btn-sm"
                onClick={onCloseDetail}
                style={{ marginLeft: '0.5rem' }}
              >
                Close
              </button>
            </div>
          </div>
          <div className="admin-info-grid">
            <span className="info-label">Query</span>
            <span className="info-value">{selectedCase.query}</span>
            <span className="info-label">Response Type</span>
            <span className="info-value">{selectedCase.expected.responseType}</span>
            <span className="info-label">Tags</span>
            <span className="info-value">{selectedCase.tags.join(', ') || 'None'}</span>
            <span className="info-label">Must Include</span>
            <span className="info-value">{selectedCase.expected.mustInclude?.join(', ') || 'None'}</span>
            <span className="info-label">Must Not Include</span>
            <span className="info-value">{selectedCase.expected.mustNotInclude?.join(', ') || 'None'}</span>
            <span className="info-label">Must Cite Sources</span>
            <span className="info-value">{String(selectedCase.expected.mustCiteSources ?? false)}</span>
            <span className="info-label">Min Confidence</span>
            <span className="info-value">{selectedCase.expected.minConfidence ?? 'N/A'}</span>
            <span className="info-label">Should Have Evidence</span>
            <span className="info-value">{String(selectedCase.expected.shouldHaveEvidence ?? false)}</span>
            <span className="info-label">Created By</span>
            <span className="info-value">{selectedCase.createdBy}</span>
            {selectedCase.sourceFeedbackId && (
              <>
                <span className="info-label">Source Feedback</span>
                <span className="info-value">{selectedCase.sourceFeedbackId}</span>
              </>
            )}
          </div>
          {selectedCase.context && (
            <div style={{ marginTop: '1rem' }}>
              <h4>Context</h4>
              <pre className="audit-detail-json" data-testid="case-context-json">
                {JSON.stringify(selectedCase.context, null, 2)}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ── Create Panel ──

interface CreatePanelProps {
  form: {
    caseId: string;
    query: string;
    responseType: string;
    mustInclude: string;
    tags: string;
    mustCiteSources: boolean;
    shouldHaveEvidence: boolean;
  };
  creating: boolean;
  error: string | null;
  success: string | null;
  onChange: (key: string, value: string | boolean) => void;
  onCreate: () => void;
}

function CreatePanel({ form, creating, error, success, onChange, onCreate }: CreatePanelProps) {
  return (
    <div data-testid="create-panel">
      <p>Create a new gold dataset evaluation case.</p>
      <div className="admin-form-inline">
        <div className="admin-form-row">
          <label>
            Case ID (eval-NNNNN)
            <input
              type="text"
              value={form.caseId}
              onChange={(e) => onChange('caseId', e.target.value)}
              placeholder="eval-00063"
              data-testid="create-case-id"
            />
          </label>
          <label>
            Response Type
            <select
              value={form.responseType}
              onChange={(e) => onChange('responseType', e.target.value)}
              data-testid="create-response-type"
            >
              <option value="final_answer">final_answer</option>
              <option value="next_steps_only">next_steps_only</option>
              <option value="escalate">escalate</option>
            </select>
          </label>
        </div>
        <div className="admin-form-row">
          <label style={{ flex: 1 }}>
            Query
            <textarea
              value={form.query}
              onChange={(e) => onChange('query', e.target.value)}
              placeholder="Natural language query (min 5 chars)"
              rows={3}
              data-testid="create-query"
              style={{ width: '100%' }}
            />
          </label>
        </div>
        <div className="admin-form-row">
          <label>
            Must Include (comma-separated)
            <input
              type="text"
              value={form.mustInclude}
              onChange={(e) => onChange('mustInclude', e.target.value)}
              placeholder="keyword1, keyword2"
              data-testid="create-must-include"
            />
          </label>
          <label>
            Tags (comma-separated)
            <input
              type="text"
              value={form.tags}
              onChange={(e) => onChange('tags', e.target.value)}
              placeholder="auth, billing"
              data-testid="create-tags"
            />
          </label>
        </div>
        <div className="admin-form-row">
          <label>
            <input
              type="checkbox"
              checked={form.mustCiteSources}
              onChange={(e) => onChange('mustCiteSources', e.target.checked)}
              data-testid="create-must-cite"
            />
            Must Cite Sources
          </label>
          <label>
            <input
              type="checkbox"
              checked={form.shouldHaveEvidence}
              onChange={(e) => onChange('shouldHaveEvidence', e.target.checked)}
              data-testid="create-should-evidence"
            />
            Should Have Evidence
          </label>
        </div>
        <div className="admin-form-actions">
          <button
            className="btn btn-primary"
            onClick={onCreate}
            disabled={creating || !form.caseId || !form.query}
            data-testid="create-submit-btn"
          >
            {creating ? 'Creating...' : 'Create Gold Case'}
          </button>
        </div>
      </div>

      {error && <div className="error-banner" role="alert" data-testid="create-error">{error}</div>}
      {success && <div className="success-banner" data-testid="create-success">{success}</div>}
    </div>
  );
}

// ── Export Panel ──

interface ExportPanelProps {
  exporting: boolean;
  error: string | null;
  onExport: () => void;
}

function ExportPanel({ exporting, error, onExport }: ExportPanelProps) {
  return (
    <div data-testid="export-panel">
      <p>Export all gold cases as JSONL for use with the evaluation CLI.</p>
      <div className="admin-form-actions">
        <button
          className="btn btn-primary"
          onClick={onExport}
          disabled={exporting}
          data-testid="export-download-btn"
        >
          {exporting ? 'Exporting...' : 'Download JSONL Export'}
        </button>
      </div>
      {error && <div className="error-banner" role="alert" data-testid="export-error">{error}</div>}
    </div>
  );
}
