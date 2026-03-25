import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  CostSettingsResponse,
  TokenUsageSummary,
  DailyUsageBreakdown,
  BudgetCheckResult,
  UpdateCostSettingsRequest,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type Tab = 'usage' | 'settings' | 'budget';

export function CostControlsPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [tab, setTab] = useState<Tab>('usage');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Usage state
  const [summary, setSummary] = useState<TokenUsageSummary | null>(null);
  const [daily, setDaily] = useState<DailyUsageBreakdown[]>([]);
  const [usageDays, setUsageDays] = useState(30);
  const [usageLoading, setUsageLoading] = useState(false);

  // Settings state
  const [settings, setSettings] = useState<CostSettingsResponse | null>(null);
  const [settingsLoading, setSettingsLoading] = useState(false);
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState<UpdateCostSettingsRequest>({});

  // Budget state
  const [budget, setBudget] = useState<BudgetCheckResult | null>(null);
  const [budgetLoading, setBudgetLoading] = useState(false);

  const loadUsage = useCallback(async () => {
    setUsageLoading(true);
    setError(null);
    try {
      const [s, d] = await Promise.all([
        api.getTokenUsageSummary(usageDays),
        api.getDailyUsage(usageDays),
      ]);
      setSummary(s);
      setDaily(d);
    } catch (e) {
      console.warn('[CostControlsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load usage data');
    } finally {
      setUsageLoading(false);
    }
  }, [usageDays]);

  const loadSettings = useCallback(async () => {
    setSettingsLoading(true);
    setError(null);
    try {
      const data = await api.getCostSettings();
      setSettings(data);
    } catch (e) {
      console.warn('[CostControlsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load cost settings');
    } finally {
      setSettingsLoading(false);
    }
  }, []);

  const loadBudget = useCallback(async () => {
    setBudgetLoading(true);
    setError(null);
    try {
      const data = await api.getBudgetCheck();
      setBudget(data);
    } catch (e) {
      console.warn('[CostControlsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load budget status');
    } finally {
      setBudgetLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!hasAdminRole(roles)) return;
    if (tab === 'usage') loadUsage();
    else if (tab === 'settings') loadSettings();
    else if (tab === 'budget') loadBudget();
  }, [roles, tab, loadUsage, loadSettings, loadBudget]);

  if (rolesLoading) {
    return <div className="admin-loading" data-testid="cost-loading"><p>Loading...</p></div>;
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="cost-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to manage cost controls.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  function startEditing() {
    if (!settings) return;
    setForm({
      dailyTokenBudget: settings.dailyTokenBudget,
      monthlyTokenBudget: settings.monthlyTokenBudget,
      maxPromptTokensPerQuery: settings.maxPromptTokensPerQuery,
      maxEvidenceChunksInPrompt: settings.maxEvidenceChunksInPrompt,
      enableEmbeddingCache: settings.enableEmbeddingCache,
      embeddingCacheTtlHours: settings.embeddingCacheTtlHours,
      enableRetrievalCompression: settings.enableRetrievalCompression,
      maxChunkCharsCompressed: settings.maxChunkCharsCompressed,
      budgetAlertThresholdPercent: settings.budgetAlertThresholdPercent,
    });
    setEditing(true);
  }

  async function handleSaveSettings() {
    setError(null);
    try {
      const updated = await api.updateCostSettings(form);
      setSettings(updated);
      setEditing(false);
      setSuccess('Cost settings updated');
    } catch (e) {
      console.warn('[CostControlsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to save settings');
    }
  }

  async function handleResetSettings() {
    if (!confirm('Reset cost settings to defaults?')) return;
    setError(null);
    try {
      await api.resetCostSettings();
      await loadSettings();
      setSuccess('Settings reset to defaults');
    } catch (e) {
      console.warn('[CostControlsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to reset settings');
    }
  }

  function formatTokens(n: number): string {
    if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
    if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`;
    return String(n);
  }

  return (
    <div className="admin-layout" data-testid="cost-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Cost Controls</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/routing" className="btn btn-sm">Routing</Link>
          <Link to="/diagnostics" className="btn btn-sm">Diagnostics</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      {error && <div className="error-banner" role="alert" data-testid="cost-error">{error}</div>}
      {success && <div className="success-banner" data-testid="cost-success">{success}</div>}

      <div className="admin-tabs">
        <button className={`admin-tab ${tab === 'usage' ? 'active' : ''}`} onClick={() => setTab('usage')} aria-label="Token Usage tab">
          Token Usage
        </button>
        <button className={`admin-tab ${tab === 'settings' ? 'active' : ''}`} onClick={() => setTab('settings')} aria-label="Settings tab">
          Settings
        </button>
        <button className={`admin-tab ${tab === 'budget' ? 'active' : ''}`} onClick={() => setTab('budget')} aria-label="Budget Status tab">
          Budget Status
        </button>
      </div>

      <main className="admin-main">
        {tab === 'usage' && (
          <div data-testid="usage-panel">
            <div className="admin-toolbar">
              <label>
                Period:{' '}
                <select value={usageDays} onChange={(e) => setUsageDays(Number(e.target.value))} aria-label="Usage period">
                  <option value={7}>7 days</option>
                  <option value={14}>14 days</option>
                  <option value={30}>30 days</option>
                  <option value={90}>90 days</option>
                </select>
              </label>
              <button className="btn btn-sm btn-primary" onClick={loadUsage} aria-label="Refresh usage data">Refresh</button>
            </div>
            {usageLoading ? (
              <p>Loading usage data...</p>
            ) : summary ? (
              <>
                <div className="admin-cards">
                  <div className="admin-card">
                    <div className="admin-card-label">Total Tokens</div>
                    <div className="admin-card-value">{formatTokens(summary.totalTokens)}</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Total Requests</div>
                    <div className="admin-card-value">{summary.totalRequests}</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Cache Hit Rate</div>
                    <div className="admin-card-value">
                      {summary.embeddingCacheHits + summary.embeddingCacheMisses > 0
                        ? `${((summary.embeddingCacheHits / (summary.embeddingCacheHits + summary.embeddingCacheMisses)) * 100).toFixed(1)}%`
                        : 'N/A'}
                    </div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Est. Cost</div>
                    <div className="admin-card-value">${Number(summary.totalEstimatedCostUsd).toFixed(2)}</div>
                  </div>
                </div>

                <div className="admin-cards">
                  <div className="admin-card">
                    <div className="admin-card-label">Daily Budget Usage</div>
                    <div className="admin-card-value">{summary.dailyBudgetUtilizationPercent.toFixed(1)}%</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Monthly Budget Usage</div>
                    <div className="admin-card-value">{summary.monthlyBudgetUtilizationPercent.toFixed(1)}%</div>
                  </div>
                </div>

                {daily.length > 0 && (
                  <>
                    <h3>Daily Breakdown</h3>
                    <table className="admin-table" data-testid="daily-usage-table" aria-label="Daily token usage breakdown">
                      <thead>
                        <tr>
                          <th>Date</th>
                          <th>Requests</th>
                          <th>Prompt Tokens</th>
                          <th>Completion Tokens</th>
                          <th>Embedding Tokens</th>
                          <th>Cache Hits</th>
                          <th>Est. Cost</th>
                        </tr>
                      </thead>
                      <tbody>
                        {daily.map((d) => (
                          <tr key={d.date}>
                            <td>{d.date}</td>
                            <td>{d.requestCount}</td>
                            <td>{formatTokens(d.promptTokens)}</td>
                            <td>{formatTokens(d.completionTokens)}</td>
                            <td>{formatTokens(d.embeddingTokens)}</td>
                            <td>{d.cacheHits}</td>
                            <td>${Number(d.estimatedCostUsd).toFixed(2)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </>
                )}
              </>
            ) : (
              <p>No usage data available.</p>
            )}
          </div>
        )}

        {tab === 'settings' && (
          <div data-testid="settings-panel">
            {settingsLoading ? (
              <p>Loading settings...</p>
            ) : settings ? (
              editing ? (
                <div className="cost-settings-form">
                  <h3>Edit Cost Settings</h3>
                  <div className="admin-info-grid">
                    <div className="draft-field">
                      <label>Daily Token Budget</label>
                      <input type="number" value={form.dailyTokenBudget ?? ''} aria-label="Daily token budget"
                        onChange={(e) => setForm({ ...form, dailyTokenBudget: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                    <div className="draft-field">
                      <label>Monthly Token Budget</label>
                      <input type="number" value={form.monthlyTokenBudget ?? ''} aria-label="Monthly token budget"
                        onChange={(e) => setForm({ ...form, monthlyTokenBudget: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                    <div className="draft-field">
                      <label>Max Prompt Tokens/Query</label>
                      <input type="number" value={form.maxPromptTokensPerQuery ?? ''} aria-label="Max prompt tokens per query"
                        onChange={(e) => setForm({ ...form, maxPromptTokensPerQuery: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                    <div className="draft-field">
                      <label>Max Evidence Chunks</label>
                      <input type="number" value={form.maxEvidenceChunksInPrompt ?? ''} aria-label="Max evidence chunks in prompt"
                        onChange={(e) => setForm({ ...form, maxEvidenceChunksInPrompt: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                    <div className="draft-field">
                      <label>Budget Alert Threshold (%)</label>
                      <input type="number" value={form.budgetAlertThresholdPercent ?? ''} aria-label="Budget alert threshold percent"
                        onChange={(e) => setForm({ ...form, budgetAlertThresholdPercent: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                    <div className="draft-field">
                      <label>Embedding Cache TTL (hours)</label>
                      <input type="number" value={form.embeddingCacheTtlHours ?? ''} aria-label="Embedding cache TTL hours"
                        onChange={(e) => setForm({ ...form, embeddingCacheTtlHours: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                    <div className="draft-field">
                      <label>Max Chunk Chars (Compressed)</label>
                      <input type="number" value={form.maxChunkCharsCompressed ?? ''} aria-label="Max chunk chars compressed"
                        onChange={(e) => setForm({ ...form, maxChunkCharsCompressed: e.target.value ? Number(e.target.value) : undefined })} />
                    </div>
                  </div>
                  <div className="admin-toggle-row">
                    <label>
                      <input type="checkbox" checked={form.enableEmbeddingCache ?? false}
                        onChange={(e) => setForm({ ...form, enableEmbeddingCache: e.target.checked })} />
                      {' '}Enable Embedding Cache
                    </label>
                    <label>
                      <input type="checkbox" checked={form.enableRetrievalCompression ?? false}
                        onChange={(e) => setForm({ ...form, enableRetrievalCompression: e.target.checked })} />
                      {' '}Enable Retrieval Compression
                    </label>
                  </div>
                  <div className="admin-form-actions">
                    <button className="btn btn-primary" onClick={handleSaveSettings} aria-label="Save cost settings">Save</button>
                    <button className="btn" onClick={() => setEditing(false)} aria-label="Cancel cost settings edit">Cancel</button>
                  </div>
                </div>
              ) : (
                <div className="cost-settings-view">
                  <div className="admin-toolbar">
                    <button className="btn btn-sm btn-primary" onClick={startEditing} aria-label="Edit cost settings">Edit Settings</button>
                    {settings.hasOverrides && (
                      <button className="btn btn-sm btn-danger-outline" onClick={handleResetSettings} aria-label="Reset cost settings to defaults">
                        Reset to Defaults
                      </button>
                    )}
                  </div>
                  <div className="admin-info-grid">
                    <div><strong>Daily Token Budget:</strong> {settings.dailyTokenBudget != null ? formatTokens(settings.dailyTokenBudget) : 'Unlimited'}</div>
                    <div><strong>Monthly Token Budget:</strong> {settings.monthlyTokenBudget != null ? formatTokens(settings.monthlyTokenBudget) : 'Unlimited'}</div>
                    <div><strong>Max Prompt Tokens/Query:</strong> {settings.maxPromptTokensPerQuery ?? 'Default'}</div>
                    <div><strong>Max Evidence Chunks:</strong> {settings.maxEvidenceChunksInPrompt}</div>
                    <div><strong>Budget Alert Threshold:</strong> {settings.budgetAlertThresholdPercent}%</div>
                    <div><strong>Embedding Cache:</strong> {settings.enableEmbeddingCache ? `Enabled (TTL: ${settings.embeddingCacheTtlHours}h)` : 'Disabled'}</div>
                    <div><strong>Retrieval Compression:</strong> {settings.enableRetrievalCompression ? `Enabled (${settings.maxChunkCharsCompressed} chars)` : 'Disabled'}</div>
                    <div><strong>Has Overrides:</strong> {settings.hasOverrides ? 'Yes' : 'No (defaults)'}</div>
                  </div>
                </div>
              )
            ) : (
              <p>No settings available.</p>
            )}
          </div>
        )}

        {tab === 'budget' && (
          <div data-testid="budget-panel">
            <div className="admin-toolbar">
              <button className="btn btn-sm btn-primary" onClick={loadBudget} aria-label="Refresh budget status">Refresh</button>
            </div>
            {budgetLoading ? (
              <p>Loading budget status...</p>
            ) : budget ? (
              <div className="budget-status">
                <div className="admin-cards">
                  <div className={`admin-card ${budget.allowed ? '' : 'card-danger'}`}>
                    <div className="admin-card-label">Status</div>
                    <div className="admin-card-value">{budget.allowed ? 'Allowed' : 'Denied'}</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Daily Utilization</div>
                    <div className="admin-card-value">{budget.dailyUtilizationPercent.toFixed(1)}%</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Monthly Utilization</div>
                    <div className="admin-card-value">{budget.monthlyUtilizationPercent.toFixed(1)}%</div>
                  </div>
                </div>
                {budget.budgetWarning && budget.warningMessage && (
                  <div className="budget-warning" data-testid="budget-warning">
                    {budget.warningMessage}
                  </div>
                )}
                {budget.denialReason && (
                  <div className="budget-denied" data-testid="budget-denied">
                    {budget.denialReason}
                  </div>
                )}
              </div>
            ) : (
              <p>No budget data available.</p>
            )}
          </div>
        )}
      </main>
    </div>
  );
}
