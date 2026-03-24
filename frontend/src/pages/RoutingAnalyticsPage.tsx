import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  RoutingAnalyticsSummary,
  RoutingRuleDto,
  RoutingRecommendationDto,
  CreateRoutingRuleRequest,
  UpdateRoutingRuleRequest,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type Tab = 'analytics' | 'rules' | 'recommendations';

export function RoutingAnalyticsPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [tab, setTab] = useState<Tab>('analytics');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Analytics state
  const [analytics, setAnalytics] = useState<RoutingAnalyticsSummary | null>(null);
  const [analyticsLoading, setAnalyticsLoading] = useState(false);
  const [windowDays, setWindowDays] = useState(30);

  // Rules state
  const [rules, setRules] = useState<RoutingRuleDto[]>([]);
  const [rulesLoading, setRulesLoading] = useState(false);
  const [showCreateRule, setShowCreateRule] = useState(false);
  const [newRule, setNewRule] = useState<CreateRoutingRuleRequest>({
    productArea: '',
    targetTeam: '',
    escalationThreshold: 0.4,
    minSeverity: 'P2',
  });
  const [editingRuleId, setEditingRuleId] = useState<string | null>(null);
  const [editRule, setEditRule] = useState<UpdateRoutingRuleRequest>({});

  // Recommendations state
  const [recommendations, setRecommendations] = useState<RoutingRecommendationDto[]>([]);
  const [recsLoading, setRecsLoading] = useState(false);
  const [recsFilter, setRecsFilter] = useState('');

  const loadAnalytics = useCallback(async () => {
    setAnalyticsLoading(true);
    setError(null);
    try {
      const data = await api.getRoutingAnalytics(windowDays);
      setAnalytics(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load analytics');
    } finally {
      setAnalyticsLoading(false);
    }
  }, [windowDays]);

  const loadRules = useCallback(async () => {
    setRulesLoading(true);
    setError(null);
    try {
      const data = await api.listRoutingRules();
      setRules(data.rules);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load rules');
    } finally {
      setRulesLoading(false);
    }
  }, []);

  const loadRecommendations = useCallback(async () => {
    setRecsLoading(true);
    setError(null);
    try {
      const data = await api.listRoutingRecommendations(recsFilter || undefined);
      setRecommendations(data.recommendations);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load recommendations');
    } finally {
      setRecsLoading(false);
    }
  }, [recsFilter]);

  useEffect(() => {
    if (!hasAdminRole(roles)) return;
    if (tab === 'analytics') loadAnalytics();
    else if (tab === 'rules') loadRules();
    else if (tab === 'recommendations') loadRecommendations();
  }, [roles, tab, loadAnalytics, loadRules, loadRecommendations]);

  if (rolesLoading) {
    return <div className="admin-loading" data-testid="routing-loading"><p>Loading...</p></div>;
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="routing-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to access routing analytics.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  async function handleCreateRule() {
    setError(null);
    try {
      const created = await api.createRoutingRule(newRule);
      setRules((prev) => [...prev, created]);
      setShowCreateRule(false);
      setNewRule({ productArea: '', targetTeam: '', escalationThreshold: 0.4, minSeverity: 'P2' });
      setSuccess('Rule created');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create rule');
    }
  }

  async function handleUpdateRule(ruleId: string) {
    setError(null);
    try {
      const updated = await api.updateRoutingRule(ruleId, editRule);
      setRules((prev) => prev.map((r) => (r.ruleId === ruleId ? updated : r)));
      setEditingRuleId(null);
      setSuccess('Rule updated');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update rule');
    }
  }

  async function handleDeleteRule(ruleId: string) {
    if (!confirm('Delete this routing rule?')) return;
    setError(null);
    try {
      await api.deleteRoutingRule(ruleId);
      setRules((prev) => prev.filter((r) => r.ruleId !== ruleId));
      setSuccess('Rule deleted');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete rule');
    }
  }

  async function handleGenerateRecs() {
    setError(null);
    try {
      const data = await api.generateRoutingRecommendations();
      setRecommendations(data.recommendations);
      setSuccess(`Generated ${data.totalCount} recommendations`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to generate recommendations');
    }
  }

  async function handleApplyRec(recId: string) {
    setError(null);
    try {
      const updated = await api.applyRoutingRecommendation(recId);
      setRecommendations((prev) =>
        prev.map((r) => (r.recommendationId === recId ? updated : r)),
      );
      setSuccess('Recommendation applied');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to apply recommendation');
    }
  }

  async function handleDismissRec(recId: string) {
    setError(null);
    try {
      await api.dismissRoutingRecommendation(recId);
      setRecommendations((prev) =>
        prev.map((r) =>
          r.recommendationId === recId ? { ...r, status: 'Dismissed' } : r,
        ),
      );
      setSuccess('Recommendation dismissed');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to dismiss recommendation');
    }
  }

  function formatPercent(val: number): string {
    return `${(val * 100).toFixed(1)}%`;
  }

  return (
    <div className="admin-layout" data-testid="routing-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Routing Analytics</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/diagnostics" className="btn btn-sm">Diagnostics</Link>
          <Link to="/playbooks" className="btn btn-sm">Playbooks</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      {error && <div className="error-banner" role="alert" data-testid="routing-error">{error}</div>}
      {success && <div className="success-banner" data-testid="routing-success">{success}</div>}

      <div className="admin-tabs">
        <button className={`admin-tab ${tab === 'analytics' ? 'active' : ''}`} onClick={() => setTab('analytics')}>
          Analytics
        </button>
        <button className={`admin-tab ${tab === 'rules' ? 'active' : ''}`} onClick={() => setTab('rules')}>
          Rules ({rules.length})
        </button>
        <button className={`admin-tab ${tab === 'recommendations' ? 'active' : ''}`} onClick={() => setTab('recommendations')}>
          Recommendations
        </button>
      </div>

      <main className="admin-main">
        {tab === 'analytics' && (
          <div data-testid="analytics-panel">
            <div className="admin-toolbar">
              <label>
                Window:{' '}
                <select value={windowDays} onChange={(e) => setWindowDays(Number(e.target.value))} aria-label="Analytics window period">
                  <option value={7}>7 days</option>
                  <option value={14}>14 days</option>
                  <option value={30}>30 days</option>
                  <option value={90}>90 days</option>
                </select>
              </label>
              <button className="btn btn-sm btn-primary" onClick={loadAnalytics}>Refresh</button>
            </div>
            {analyticsLoading ? (
              <p>Loading analytics...</p>
            ) : analytics ? (
              <>
                <div className="admin-cards">
                  <div className="admin-card">
                    <div className="admin-card-label">Total Outcomes</div>
                    <div className="admin-card-value">{analytics.totalOutcomes}</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Self-Resolution Rate</div>
                    <div className="admin-card-value">{formatPercent(analytics.selfResolutionRate)}</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Acceptance Rate</div>
                    <div className="admin-card-value">{formatPercent(analytics.overallAcceptanceRate)}</div>
                  </div>
                  <div className="admin-card">
                    <div className="admin-card-label">Reroute Rate</div>
                    <div className="admin-card-value">{formatPercent(analytics.overallRerouteRate)}</div>
                  </div>
                </div>

                {analytics.teamMetrics.length > 0 && (
                  <>
                    <h3>Team Metrics</h3>
                    <table className="admin-table" data-testid="team-metrics-table" aria-label="Team routing metrics">
                      <thead>
                        <tr>
                          <th>Team</th>
                          <th>Escalations</th>
                          <th>Accepted</th>
                          <th>Rerouted</th>
                          <th>Acceptance Rate</th>
                          <th>Reroute Rate</th>
                        </tr>
                      </thead>
                      <tbody>
                        {analytics.teamMetrics.map((tm) => (
                          <tr key={tm.targetTeam}>
                            <td>{tm.targetTeam}</td>
                            <td>{tm.totalEscalations}</td>
                            <td>{tm.acceptedCount}</td>
                            <td>{tm.reroutedCount}</td>
                            <td>{formatPercent(tm.acceptanceRate)}</td>
                            <td>{formatPercent(tm.rerouteRate)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </>
                )}

                {analytics.productAreaMetrics.length > 0 && (
                  <>
                    <h3>Product Area Metrics</h3>
                    <table className="admin-table" data-testid="product-area-table" aria-label="Product area routing metrics">
                      <thead>
                        <tr>
                          <th>Product Area</th>
                          <th>Target Team</th>
                          <th>Escalations</th>
                          <th>Acceptance Rate</th>
                          <th>Reroute Rate</th>
                        </tr>
                      </thead>
                      <tbody>
                        {analytics.productAreaMetrics.map((pa) => (
                          <tr key={pa.productArea}>
                            <td>{pa.productArea}</td>
                            <td>{pa.currentTargetTeam}</td>
                            <td>{pa.totalEscalations}</td>
                            <td>{formatPercent(pa.acceptanceRate)}</td>
                            <td>{formatPercent(pa.rerouteRate)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </>
                )}
              </>
            ) : (
              <p>No analytics data available.</p>
            )}
          </div>
        )}

        {tab === 'rules' && (
          <div data-testid="rules-panel">
            <div className="admin-toolbar">
              <button className="btn btn-sm btn-primary" onClick={() => setShowCreateRule(true)}>
                New Rule
              </button>
            </div>
            {showCreateRule && (
              <div className="admin-form-inline" data-testid="create-rule-form">
                <div className="admin-form-row">
                  <input placeholder="Product Area" aria-label="Product area" value={newRule.productArea}
                    onChange={(e) => setNewRule({ ...newRule, productArea: e.target.value })} />
                  <input placeholder="Target Team" aria-label="Target team" value={newRule.targetTeam}
                    onChange={(e) => setNewRule({ ...newRule, targetTeam: e.target.value })} />
                  <input type="number" step="0.1" placeholder="Threshold" aria-label="Escalation threshold" value={newRule.escalationThreshold}
                    onChange={(e) => setNewRule({ ...newRule, escalationThreshold: parseFloat(e.target.value) })} />
                  <select value={newRule.minSeverity} aria-label="Minimum severity"
                    onChange={(e) => setNewRule({ ...newRule, minSeverity: e.target.value })}>
                    <option value="P1">P1</option>
                    <option value="P2">P2</option>
                    <option value="P3">P3</option>
                    <option value="P4">P4</option>
                  </select>
                  <button className="btn btn-sm btn-primary" onClick={handleCreateRule}>Create</button>
                  <button className="btn btn-sm" onClick={() => setShowCreateRule(false)}>Cancel</button>
                </div>
              </div>
            )}
            {rulesLoading ? (
              <p>Loading rules...</p>
            ) : (
              <table className="admin-table" data-testid="rules-table" aria-label="Escalation routing rules">
                <thead>
                  <tr>
                    <th>Product Area</th>
                    <th>Target Team</th>
                    <th>Threshold</th>
                    <th>Min Severity</th>
                    <th>Active</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rules.map((rule) => (
                    <tr key={rule.ruleId}>
                      {editingRuleId === rule.ruleId ? (
                        <>
                          <td>{rule.productArea}</td>
                          <td>
                            <input value={editRule.targetTeam ?? rule.targetTeam}
                              onChange={(e) => setEditRule({ ...editRule, targetTeam: e.target.value })}
                              aria-label="Edit target team" />
                          </td>
                          <td>
                            <input type="number" step="0.1"
                              value={editRule.escalationThreshold ?? rule.escalationThreshold}
                              onChange={(e) => setEditRule({ ...editRule, escalationThreshold: parseFloat(e.target.value) })}
                              aria-label="Edit escalation threshold" />
                          </td>
                          <td>
                            <select value={editRule.minSeverity ?? rule.minSeverity}
                              onChange={(e) => setEditRule({ ...editRule, minSeverity: e.target.value })}
                              aria-label="Edit minimum severity">
                              <option value="P1">P1</option>
                              <option value="P2">P2</option>
                              <option value="P3">P3</option>
                              <option value="P4">P4</option>
                            </select>
                          </td>
                          <td>{rule.isActive ? 'Yes' : 'No'}</td>
                          <td>
                            <button className="btn btn-sm btn-primary" onClick={() => handleUpdateRule(rule.ruleId)}>Save</button>
                            <button className="btn btn-sm" onClick={() => setEditingRuleId(null)}>Cancel</button>
                          </td>
                        </>
                      ) : (
                        <>
                          <td>{rule.productArea}</td>
                          <td>{rule.targetTeam}</td>
                          <td>{rule.escalationThreshold}</td>
                          <td>{rule.minSeverity}</td>
                          <td>{rule.isActive ? 'Yes' : 'No'}</td>
                          <td>
                            <button className="btn btn-sm" onClick={() => { setEditingRuleId(rule.ruleId); setEditRule({}); }}>Edit</button>
                            <button className="btn btn-sm btn-danger-outline" onClick={() => handleDeleteRule(rule.ruleId)}>Delete</button>
                          </td>
                        </>
                      )}
                    </tr>
                  ))}
                  {rules.length === 0 && (
                    <tr><td colSpan={6} className="admin-empty">No routing rules configured.</td></tr>
                  )}
                </tbody>
              </table>
            )}
          </div>
        )}

        {tab === 'recommendations' && (
          <div data-testid="recommendations-panel">
            <div className="admin-toolbar">
              <select value={recsFilter} onChange={(e) => setRecsFilter(e.target.value)} aria-label="Filter by recommendation status">
                <option value="">All</option>
                <option value="Pending">Pending</option>
                <option value="Applied">Applied</option>
                <option value="Dismissed">Dismissed</option>
              </select>
              <button className="btn btn-sm btn-primary" onClick={handleGenerateRecs}>
                Generate Recommendations
              </button>
            </div>
            {recsLoading ? (
              <p>Loading recommendations...</p>
            ) : (
              <table className="admin-table" data-testid="recommendations-table" aria-label="Routing recommendations">
                <thead>
                  <tr>
                    <th>Type</th>
                    <th>Product Area</th>
                    <th>Current Team</th>
                    <th>Suggestion</th>
                    <th>Confidence</th>
                    <th>Source</th>
                    <th>Status</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {recommendations.map((rec) => (
                    <tr key={rec.recommendationId}>
                      <td>{rec.recommendationType}</td>
                      <td>{rec.productArea}</td>
                      <td>{rec.currentTargetTeam}</td>
                      <td>
                        {rec.suggestedTargetTeam
                          ? `Team: ${rec.suggestedTargetTeam}`
                          : rec.suggestedThreshold != null
                            ? `Threshold: ${rec.suggestedThreshold}`
                            : '-'}
                      </td>
                      <td>{formatPercent(rec.confidence)}</td>
                      <td>
                        {rec.sourceEvalReportId ? (
                          <span className="eval-report-link" title={`Eval report: ${rec.sourceEvalReportId}`}>
                            Eval Report
                          </span>
                        ) : (
                          <span className="source-manual">Manual</span>
                        )}
                      </td>
                      <td>
                        <span className={`rec-status rec-status-${rec.status.toLowerCase()}`}>
                          {rec.status}
                        </span>
                      </td>
                      <td>
                        {rec.status === 'Pending' && (
                          <>
                            <button className="btn btn-sm btn-primary" onClick={() => handleApplyRec(rec.recommendationId)}>
                              Apply
                            </button>
                            <button className="btn btn-sm" onClick={() => handleDismissRec(rec.recommendationId)}>
                              Dismiss
                            </button>
                          </>
                        )}
                      </td>
                    </tr>
                  ))}
                  {recommendations.length === 0 && (
                    <tr><td colSpan={8} className="admin-empty">No recommendations.</td></tr>
                  )}
                </tbody>
              </table>
            )}
          </div>
        )}
      </main>
    </div>
  );
}
