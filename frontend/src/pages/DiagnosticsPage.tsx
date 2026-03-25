import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { formatRelativeTime } from '../utils/dateFormat';
import type {
  DeadLetterListResponse,
  DeadLetterMessage,
  DiagnosticsSummaryResponse,
  SecretsStatusResponse,
  SloStatusResponse,
  WebhookStatusListResponse,
  WebhookSubscriptionStatus,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type Tab = 'overview' | 'webhooks' | 'dead-letters';

export function DiagnosticsPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [tab, setTab] = useState<Tab>('overview');
  const [summary, setSummary] = useState<DiagnosticsSummaryResponse | null>(null);
  const [webhooks, setWebhooks] = useState<WebhookStatusListResponse | null>(null);
  const [deadLetters, setDeadLetters] = useState<DeadLetterListResponse | null>(null);
  const [sloStatus, setSloStatus] = useState<SloStatusResponse | null>(null);
  const [secretsStatus, setSecretsStatus] = useState<SecretsStatusResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadOverview = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [sum, slo, secrets] = await Promise.all([
        api.getDiagnosticsSummary(),
        api.getSloStatus(),
        api.getSecretsStatus(),
      ]);
      setSummary(sum);
      setSloStatus(slo);
      setSecretsStatus(secrets);
    } catch (e) {
      console.warn('[DiagnosticsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load diagnostics');
    } finally {
      setLoading(false);
    }
  }, []);

  const loadWebhooks = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.getAllWebhooks();
      setWebhooks(result);
    } catch (e) {
      console.warn('[DiagnosticsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load webhooks');
    } finally {
      setLoading(false);
    }
  }, []);

  const loadDeadLetters = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.getDeadLetters(50);
      setDeadLetters(result);
    } catch (e) {
      console.warn('[DiagnosticsPage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load dead letters');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!hasAdminRole(roles)) return;
    if (tab === 'overview') loadOverview();
    else if (tab === 'webhooks') loadWebhooks();
    else if (tab === 'dead-letters') loadDeadLetters();
  }, [roles, tab, loadOverview, loadWebhooks, loadDeadLetters]);

  if (rolesLoading) {
    return (
      <div className="admin-loading" data-testid="diag-loading">
        <p>Loading...</p>
      </div>
    );
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="diag-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to access diagnostics.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  return (
    <div className="admin-layout" data-testid="diagnostics-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>System Diagnostics</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm" data-testid="admin-link">Connectors</Link>
          <Link to="/patterns" className="btn btn-sm">Patterns</Link>
          <Link to="/audit" className="btn btn-sm" data-testid="audit-link">Audit</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      <div className="diag-tabs" data-testid="diag-tabs">
        <button
          className={`diag-tab${tab === 'overview' ? ' diag-tab-active' : ''}`}
          onClick={() => setTab('overview')}
          data-testid="tab-overview"
          aria-label="Overview tab"
        >
          Overview
        </button>
        <button
          className={`diag-tab${tab === 'webhooks' ? ' diag-tab-active' : ''}`}
          onClick={() => setTab('webhooks')}
          data-testid="tab-webhooks"
          aria-label="Webhooks tab"
        >
          Webhooks
          {summary && summary.fallbackWebhooks > 0 && (
            <span className="diag-badge diag-badge-warn" data-testid="webhook-badge">
              {summary.fallbackWebhooks}
            </span>
          )}
        </button>
        <button
          className={`diag-tab${tab === 'dead-letters' ? ' diag-tab-active' : ''}`}
          onClick={() => setTab('dead-letters')}
          data-testid="tab-dead-letters"
          aria-label="Dead Letters tab"
        >
          Dead Letters
          {deadLetters && deadLetters.count > 0 && (
            <span className="diag-badge diag-badge-danger" data-testid="dl-badge">
              {deadLetters.count}
            </span>
          )}
        </button>
      </div>

      {error && (
        <div className="error-banner" role="alert" data-testid="diag-error">{error}</div>
      )}

      <main className="admin-main">
        {loading ? (
          <div className="admin-loading"><p>Loading...</p></div>
        ) : tab === 'overview' ? (
          <OverviewPanel
            summary={summary}
            sloStatus={sloStatus}
            secretsStatus={secretsStatus}
          />
        ) : tab === 'webhooks' ? (
          <WebhookPanel webhooks={webhooks} />
        ) : (
          <DeadLetterPanel deadLetters={deadLetters} />
        )}
      </main>
    </div>
  );
}

function OverviewPanel({
  summary,
  sloStatus,
  secretsStatus,
}: {
  summary: DiagnosticsSummaryResponse | null;
  sloStatus: SloStatusResponse | null;
  secretsStatus: SecretsStatusResponse | null;
}) {
  if (!summary) return null;

  return (
    <div className="diag-overview" data-testid="overview-panel">
      <div className="diag-cards">
        <div className="diag-card" data-testid="card-connectors">
          <h3>Connectors</h3>
          <div className="diag-stat">{summary.enabledConnectors} / {summary.totalConnectors}</div>
          <div className="diag-label">enabled</div>
        </div>
        <div className="diag-card" data-testid="card-webhooks">
          <h3>Webhooks</h3>
          <div className="diag-stat">{summary.activeWebhooks} / {summary.totalWebhooks}</div>
          <div className="diag-label">active</div>
          {summary.fallbackWebhooks > 0 && (
            <div className="diag-warn">{summary.fallbackWebhooks} in fallback</div>
          )}
        </div>
        <div className="diag-card" data-testid="card-rate-limits">
          <h3>Rate Limits</h3>
          {summary.rateLimitAlertingConnectors > 0 ? (
            <div className="diag-danger-text" data-testid="rate-limit-alert-count">
              {summary.rateLimitAlertingConnectors} connector{summary.rateLimitAlertingConnectors !== 1 ? 's' : ''} throttled
            </div>
          ) : (
            <div className="diag-label">No rate-limit alerts</div>
          )}
        </div>
        <div className="diag-card" data-testid="card-credentials">
          <h3>Credentials</h3>
          {(summary.credentialExpired > 0 || summary.credentialCritical > 0 || summary.credentialWarnings > 0) ? (
            <>
              {summary.credentialExpired > 0 && (
                <div className="diag-danger-text">{summary.credentialExpired} expired</div>
              )}
              {summary.credentialCritical > 0 && (
                <div className="diag-danger-text">{summary.credentialCritical} critical</div>
              )}
              {summary.credentialWarnings > 0 && (
                <div className="diag-warn">{summary.credentialWarnings} warning(s)</div>
              )}
            </>
          ) : (
            <div className="diag-label">All healthy</div>
          )}
        </div>
        <div className="diag-card" data-testid="card-services">
          <h3>Services</h3>
          <ServiceStatus label="Service Bus" ok={summary.serviceBusConfigured} />
          <ServiceStatus label="Key Vault" ok={summary.keyVaultConfigured} />
          <ServiceStatus label="OpenAI" ok={summary.openAiConfigured} />
          <ServiceStatus label="Search" ok={summary.searchServiceConfigured} />
        </div>
      </div>

      {sloStatus && (
        <div className="diag-section" data-testid="slo-targets">
          <h3>SLO Targets</h3>
          <table className="diag-table" aria-label="SLO targets">
            <thead>
              <tr><th>Metric</th><th>Target</th></tr>
            </thead>
            <tbody>
              <tr><td>Answer Latency P95</td><td>{sloStatus.targets.answerLatencyP95TargetMs}ms</td></tr>
              <tr><td>Availability</td><td>{sloStatus.targets.availabilityTargetPercent}%</td></tr>
              <tr><td>Sync Lag P95</td><td>{sloStatus.targets.syncLagP95TargetMinutes}min</td></tr>
              <tr><td>No-Evidence Rate</td><td>{(sloStatus.targets.noEvidenceRateThreshold * 100).toFixed(0)}%</td></tr>
              <tr><td>Dead-Letter Depth</td><td>{sloStatus.targets.deadLetterDepthThreshold}</td></tr>
              <tr><td>Rate-Limit Alert</td><td>{sloStatus.targets.rateLimitAlertThreshold} hits / {sloStatus.targets.rateLimitAlertWindowMinutes}min</td></tr>
            </tbody>
          </table>
        </div>
      )}

      {secretsStatus && (
        <div className="diag-section" data-testid="secrets-status">
          <h3>Secrets Status</h3>
          <ServiceStatus label="Key Vault" ok={secretsStatus.keyVaultConfigured} />
          <ServiceStatus label="OpenAI Key" ok={secretsStatus.openAiKeyConfigured} />
          <div className="diag-label">Model: {secretsStatus.openAiModel}</div>
        </div>
      )}

      {summary.connectorHealth.length > 0 && (
        <div className="diag-section" data-testid="connector-health">
          <h3>Connector Health</h3>
          <table className="diag-table" aria-label="Connector health">
            <thead>
              <tr>
                <th>Name</th><th>Type</th><th>Status</th>
                <th>Last Sync</th><th>Webhooks</th><th>Failures</th><th>Rate Limits</th>
              </tr>
            </thead>
            <tbody>
              {summary.connectorHealth.map((c) => (
                <tr key={c.connectorId}>
                  <td>{c.name}</td>
                  <td>{c.connectorType}</td>
                  <td><StatusBadge status={c.status} /></td>
                  <td>
                    {c.lastSyncStatus
                      ? <><StatusBadge status={c.lastSyncStatus} /> {c.lastSyncAt ? formatTime(c.lastSyncAt) : ''}</>
                      : 'Never'}
                  </td>
                  <td>
                    {c.webhookCount}
                    {c.webhooksInFallback > 0 && (
                      <span className="diag-warn-inline"> ({c.webhooksInFallback} fallback)</span>
                    )}
                  </td>
                  <td className={c.totalFailures > 0 ? 'diag-danger-text' : ''}>
                    {c.totalFailures}
                  </td>
                  <td data-testid={`rate-limit-${c.connectorId}`}>
                    {c.rateLimitAlerting ? (
                      <span className="diag-badge-inline badge-warn" data-testid={`rate-limit-badge-${c.connectorId}`}>
                        {c.rateLimitHits} hits
                      </span>
                    ) : (
                      <span className="diag-muted">0</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function WebhookPanel({ webhooks }: { webhooks: WebhookStatusListResponse | null }) {
  if (!webhooks) return null;

  return (
    <div className="diag-webhooks" data-testid="webhook-panel">
      <div className="diag-summary-row">
        <span>Total: {webhooks.totalCount}</span>
        <span>Active: {webhooks.activeCount}</span>
        <span className={webhooks.fallbackCount > 0 ? 'diag-warn' : ''}>
          Fallback: {webhooks.fallbackCount}
        </span>
      </div>

      {webhooks.subscriptions.length === 0 ? (
        <p className="diag-empty">No webhook subscriptions registered.</p>
      ) : (
        <table className="diag-table" data-testid="webhook-table" aria-label="Webhook subscriptions">
          <thead>
            <tr>
              <th>Connector</th><th>Event Type</th><th>Status</th>
              <th>Failures</th><th>Last Delivery</th><th>Next Poll</th>
            </tr>
          </thead>
          <tbody>
            {webhooks.subscriptions.map((w: WebhookSubscriptionStatus) => (
              <tr key={w.id} data-testid={`webhook-row-${w.id}`}>
                <td>{w.connectorName} <span className="diag-muted">({w.connectorType})</span></td>
                <td>{w.eventType}</td>
                <td><WebhookStatusBadge sub={w} /></td>
                <td className={w.consecutiveFailures > 0 ? 'diag-danger-text' : ''}>
                  {w.consecutiveFailures}
                </td>
                <td>{w.lastDeliveryAt ? formatTime(w.lastDeliveryAt) : 'Never'}</td>
                <td>{w.nextPollAt ? formatTime(w.nextPollAt) : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function DeadLetterPanel({ deadLetters }: { deadLetters: DeadLetterListResponse | null }) {
  const [expanded, setExpanded] = useState<string | null>(null);

  if (!deadLetters) return null;

  return (
    <div className="diag-dead-letters" data-testid="dead-letter-panel">
      <div className="diag-summary-row">
        <span>Messages: {deadLetters.count}</span>
      </div>

      {deadLetters.count === 0 ? (
        <p className="diag-empty" data-testid="dl-empty">No dead-letter messages.</p>
      ) : (
        <table className="diag-table" data-testid="dead-letter-table" aria-label="Dead-letter messages">
          <thead>
            <tr>
              <th>Message ID</th><th>Subject</th><th>Reason</th>
              <th>Deliveries</th><th>Enqueued</th><th></th>
            </tr>
          </thead>
          <tbody>
            {deadLetters.messages.map((m: DeadLetterMessage) => (
              <React.Fragment key={m.messageId}>
                <tr data-testid={`dl-row-${m.messageId}`}>
                  <td className="diag-mono">{m.messageId.substring(0, 8)}...</td>
                  <td>{m.subject ?? '-'}</td>
                  <td>{m.deadLetterReason ?? 'Unknown'}</td>
                  <td>{m.deliveryCount}</td>
                  <td>{formatTime(m.enqueuedTime)}</td>
                  <td>
                    <button
                      className="btn btn-sm"
                      onClick={() => setExpanded(expanded === m.messageId ? null : m.messageId)}
                      data-testid={`dl-expand-${m.messageId}`}
                      aria-label={expanded === m.messageId ? 'Hide dead-letter details' : 'Show dead-letter details'}
                    >
                      {expanded === m.messageId ? 'Hide' : 'Details'}
                    </button>
                  </td>
                </tr>
                {expanded === m.messageId && (
                  <tr key={`${m.messageId}-detail`}>
                    <td colSpan={6}>
                      <div className="diag-detail-box" data-testid={`dl-detail-${m.messageId}`}>
                        <div><strong>Correlation ID:</strong> {m.correlationId ?? '-'}</div>
                        <div><strong>Error:</strong> {m.deadLetterErrorDescription ?? '-'}</div>
                        <div><strong>Body:</strong></div>
                        <pre className="diag-pre">{m.body}</pre>
                      </div>
                    </td>
                  </tr>
                )}
              </React.Fragment>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function ServiceStatus({ label, ok }: { label: string; ok: boolean }) {
  return (
    <div className={`diag-service-status ${ok ? 'diag-ok' : 'diag-not-ok'}`}>
      <span className="diag-dot" /> {label}
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const cls = status === 'Enabled' || status === 'Completed' ? 'badge-success'
    : status === 'Failed' ? 'badge-danger'
    : status === 'Running' ? 'badge-info'
    : 'badge-muted';
  return <span className={`diag-badge-inline ${cls}`}>{status}</span>;
}

function WebhookStatusBadge({ sub }: { sub: WebhookSubscriptionStatus }) {
  if (sub.pollingFallbackActive) return <span className="diag-badge-inline badge-warn">Fallback</span>;
  if (!sub.isActive) return <span className="diag-badge-inline badge-muted">Inactive</span>;
  if (sub.consecutiveFailures > 0) return <span className="diag-badge-inline badge-warn">Degraded</span>;
  return <span className="diag-badge-inline badge-success">Healthy</span>;
}

const formatTime = formatRelativeTime;
