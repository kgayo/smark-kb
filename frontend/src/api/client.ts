import type {
  ApiResponse,
  ApplyRecommendationRequest,
  ApproveEscalationDraftRequest,
  ApprovePatternRequest,
  BudgetCheckResult,
  ConnectorCredentialStatus,
  ConnectorListResponse,
  ConnectorResponse,
  ConnectorValidationResult,
  CredentialRotationResult,
  CredentialStatusSummary,
  CostSettingsResponse,
  CreateConnectorRequest,
  CreateEscalationDraftRequest,
  CreateRoutingRuleRequest,
  CreateSessionRequest,
  CreateSynonymRuleRequest,
  CreateTeamPlaybookRequest,
  DailyUsageBreakdown,
  DataSubjectDeletionListResponse,
  DataSubjectDeletionRequest,
  DataSubjectDeletionResponse,
  DeadLetterListResponse,
  DeprecatePatternRequest,
  DiagnosticsSummaryResponse,
  EscalationDraftExportResponse,
  EscalationDraftResponse,
  ExternalEscalationResult,
  FeedbackResponse,
  MessageListResponse,
  OutcomeListResponse,
  OutcomeResponse,
  PatternDetail,
  PatternGovernanceQueueResponse,
  PatternGovernanceResult,
  PatternUsageMetrics,
  PatternVersionHistoryResponse,
  PiiPolicyResponse,
  PiiPolicyUpdateRequest,
  RecordOutcomeRequest,
  RetentionCleanupResult,
  RetentionComplianceReport,
  RetentionExecutionHistoryResponse,
  RetentionPolicyEntry,
  RetentionPolicyResponse,
  RetentionPolicyUpdateRequest,
  RetrievalSettingsResponse,
  ReviewPatternRequest,
  RoutingAnalyticsSummary,
  RoutingRecommendationDto,
  RoutingRecommendationListResponse,
  RoutingRuleDto,
  RoutingRuleListResponse,
  SecretsStatusResponse,
  SendMessageRequest,
  SessionChatResponse,
  SessionListResponse,
  SessionResponse,
  SloStatusResponse,
  SubmitFeedbackRequest,
  SyncNowRequest,
  SyncRunListResponse,
  SyncRunSummary,
  SynonymMapSyncResult,
  SynonymRuleListResponse,
  SynonymRuleResponse,
  TeamPlaybookDto,
  TeamPlaybookListResponse,
  TestConnectionResponse,
  TokenUsageSummary,
  UpdateConnectorRequest,
  UpdateCostSettingsRequest,
  UpdateEscalationDraftRequest,
  UpdateRetrievalSettingsRequest,
  UpdateRoutingRuleRequest,
  UpdateSynonymRuleRequest,
  UpdateTeamPlaybookRequest,
  AuditEventListResponse,
  AuditEventQueryParams,
  AuditExportParams,
  WebhookStatusListResponse,
} from './types';

let getAccessToken: (() => Promise<string | null>) | null = null;

export function setTokenProvider(provider: () => Promise<string | null>): void {
  getAccessToken = provider;
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init?.headers as Record<string, string>),
  };

  if (getAccessToken) {
    const token = await getAccessToken();
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
  }

  const res = await fetch(path, { ...init, headers });

  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new ApiError(res.status, body || res.statusText);
  }

  return res.json() as Promise<T>;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly detail: string,
  ) {
    super(`API ${status}: ${detail}`);
    this.name = 'ApiError';
  }
}

function unwrap<T>(response: ApiResponse<T>): T {
  if (!response.isSuccess || response.data === null) {
    throw new ApiError(0, response.error ?? 'Unknown error');
  }
  return response.data;
}

// ── Session endpoints ──

export async function createSession(req?: CreateSessionRequest): Promise<SessionResponse> {
  const res = await apiFetch<ApiResponse<SessionResponse>>('/api/sessions', {
    method: 'POST',
    body: JSON.stringify(req ?? {}),
  });
  return unwrap(res);
}

export async function listSessions(): Promise<SessionListResponse> {
  const res = await apiFetch<ApiResponse<SessionListResponse>>('/api/sessions');
  return unwrap(res);
}

export async function getSession(sessionId: string): Promise<SessionResponse> {
  const res = await apiFetch<ApiResponse<SessionResponse>>(`/api/sessions/${sessionId}`);
  return unwrap(res);
}

export async function deleteSession(sessionId: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(`/api/sessions/${sessionId}`, { method: 'DELETE' });
}

export async function getMessages(sessionId: string): Promise<MessageListResponse> {
  const res = await apiFetch<ApiResponse<MessageListResponse>>(
    `/api/sessions/${sessionId}/messages`,
  );
  return unwrap(res);
}

export async function sendMessage(
  sessionId: string,
  req: SendMessageRequest,
): Promise<SessionChatResponse> {
  const res = await apiFetch<ApiResponse<SessionChatResponse>>(
    `/api/sessions/${sessionId}/messages`,
    {
      method: 'POST',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

// ── Escalation draft endpoints ──

export async function createEscalationDraft(
  req: CreateEscalationDraftRequest,
): Promise<EscalationDraftResponse> {
  const res = await apiFetch<ApiResponse<EscalationDraftResponse>>(
    '/api/escalations/draft',
    {
      method: 'POST',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

export async function getEscalationDraft(
  draftId: string,
): Promise<EscalationDraftResponse> {
  const res = await apiFetch<ApiResponse<EscalationDraftResponse>>(
    `/api/escalations/draft/${draftId}`,
  );
  return unwrap(res);
}

export async function updateEscalationDraft(
  draftId: string,
  req: UpdateEscalationDraftRequest,
): Promise<EscalationDraftResponse> {
  const res = await apiFetch<ApiResponse<EscalationDraftResponse>>(
    `/api/escalations/draft/${draftId}`,
    {
      method: 'PUT',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

export async function exportEscalationDraft(
  draftId: string,
): Promise<EscalationDraftExportResponse> {
  const res = await apiFetch<ApiResponse<EscalationDraftExportResponse>>(
    `/api/escalations/draft/${draftId}/export`,
  );
  return unwrap(res);
}

export async function deleteEscalationDraft(draftId: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(`/api/escalations/draft/${draftId}`, {
    method: 'DELETE',
  });
}

export async function approveEscalationDraft(
  draftId: string,
  req: ApproveEscalationDraftRequest,
): Promise<ExternalEscalationResult> {
  const res = await apiFetch<ApiResponse<ExternalEscalationResult>>(
    `/api/escalations/draft/${draftId}/approve`,
    {
      method: 'POST',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

// ── Feedback endpoints ──

export async function submitFeedback(
  sessionId: string,
  messageId: string,
  req: SubmitFeedbackRequest,
): Promise<FeedbackResponse> {
  const res = await apiFetch<ApiResponse<FeedbackResponse>>(
    `/api/sessions/${sessionId}/messages/${messageId}/feedback`,
    {
      method: 'POST',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

export async function getFeedback(
  sessionId: string,
  messageId: string,
): Promise<FeedbackResponse | null> {
  try {
    const res = await apiFetch<ApiResponse<FeedbackResponse>>(
      `/api/sessions/${sessionId}/messages/${messageId}/feedback`,
    );
    return unwrap(res);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

// ── Outcome endpoints ──

export async function recordOutcome(
  sessionId: string,
  req: RecordOutcomeRequest,
): Promise<OutcomeResponse> {
  const res = await apiFetch<ApiResponse<OutcomeResponse>>(
    `/api/sessions/${sessionId}/outcome`,
    {
      method: 'POST',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

export async function getOutcomes(
  sessionId: string,
): Promise<OutcomeListResponse> {
  const res = await apiFetch<ApiResponse<OutcomeListResponse>>(
    `/api/sessions/${sessionId}/outcome`,
  );
  return unwrap(res);
}

// ── Connector admin endpoints ──

export async function listConnectors(): Promise<ConnectorListResponse> {
  const res = await apiFetch<ApiResponse<ConnectorListResponse>>('/api/admin/connectors');
  return unwrap(res);
}

export async function getConnector(connectorId: string): Promise<ConnectorResponse> {
  const res = await apiFetch<ApiResponse<ConnectorResponse>>(
    `/api/admin/connectors/${connectorId}`,
  );
  return unwrap(res);
}

export async function createConnector(
  req: CreateConnectorRequest,
): Promise<ConnectorResponse> {
  const res = await apiFetch<ApiResponse<ConnectorResponse>>('/api/admin/connectors', {
    method: 'POST',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function updateConnector(
  connectorId: string,
  req: UpdateConnectorRequest,
): Promise<ConnectorResponse> {
  const res = await apiFetch<ApiResponse<ConnectorResponse>>(
    `/api/admin/connectors/${connectorId}`,
    {
      method: 'PUT',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

export async function deleteConnector(connectorId: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(`/api/admin/connectors/${connectorId}`, {
    method: 'DELETE',
  });
}

export async function enableConnector(connectorId: string): Promise<ConnectorResponse> {
  const res = await apiFetch<ApiResponse<ConnectorResponse>>(
    `/api/admin/connectors/${connectorId}/enable`,
    { method: 'POST' },
  );
  return unwrap(res);
}

export async function disableConnector(connectorId: string): Promise<ConnectorResponse> {
  const res = await apiFetch<ApiResponse<ConnectorResponse>>(
    `/api/admin/connectors/${connectorId}/disable`,
    { method: 'POST' },
  );
  return unwrap(res);
}

export async function testConnection(
  connectorId: string,
): Promise<TestConnectionResponse> {
  const res = await apiFetch<ApiResponse<TestConnectionResponse>>(
    `/api/admin/connectors/${connectorId}/test`,
    { method: 'POST' },
  );
  return unwrap(res);
}

export async function syncNow(
  connectorId: string,
  req: SyncNowRequest,
): Promise<{ syncRunId: string; status: string }> {
  const res = await apiFetch<ApiResponse<{ syncRunId: string; status: string }>>(
    `/api/admin/connectors/${connectorId}/sync-now`,
    {
      method: 'POST',
      body: JSON.stringify(req),
    },
  );
  return unwrap(res);
}

export async function listSyncRuns(connectorId: string): Promise<SyncRunListResponse> {
  const res = await apiFetch<ApiResponse<SyncRunListResponse>>(
    `/api/admin/connectors/${connectorId}/sync-runs`,
  );
  return unwrap(res);
}

export async function getSyncRun(
  connectorId: string,
  syncRunId: string,
): Promise<SyncRunSummary> {
  const res = await apiFetch<ApiResponse<SyncRunSummary>>(
    `/api/admin/connectors/${connectorId}/sync-runs/${syncRunId}`,
  );
  return unwrap(res);
}

export async function validateMapping(
  connectorId: string,
  mapping: { rules: Array<{ sourceField: string; targetField: string; transform: string; transformExpression: string | null; isRequired: boolean; defaultValue: string | null }> },
): Promise<ConnectorValidationResult> {
  const res = await apiFetch<ApiResponse<ConnectorValidationResult>>(
    `/api/admin/connectors/${connectorId}/validate-mapping`,
    {
      method: 'POST',
      body: JSON.stringify(mapping),
    },
  );
  return unwrap(res);
}

// ── User info endpoint ──

export interface UserInfo {
  userId: string | null;
  name: string | null;
  tenantId: string | null;
  correlationId: string | null;
  roles: string[];
}

export async function getMe(): Promise<UserInfo> {
  return apiFetch<UserInfo>('/api/me');
}

// ── Pattern governance endpoints (P1-006) ──

export async function getGovernanceQueue(
  trustLevel?: string,
  productArea?: string,
  page?: number,
  pageSize?: number,
): Promise<PatternGovernanceQueueResponse> {
  const params = new URLSearchParams();
  if (trustLevel) params.set('trustLevel', trustLevel);
  if (productArea) params.set('productArea', productArea);
  if (page) params.set('page', String(page));
  if (pageSize) params.set('pageSize', String(pageSize));
  const qs = params.toString();
  const res = await apiFetch<ApiResponse<PatternGovernanceQueueResponse>>(
    `/api/patterns/governance-queue${qs ? `?${qs}` : ''}`,
  );
  return unwrap(res);
}

export async function getPatternDetail(patternId: string): Promise<PatternDetail> {
  const res = await apiFetch<ApiResponse<PatternDetail>>(
    `/api/patterns/${encodeURIComponent(patternId)}`,
  );
  return unwrap(res);
}

export async function reviewPattern(
  patternId: string,
  req: ReviewPatternRequest,
): Promise<PatternGovernanceResult> {
  const res = await apiFetch<ApiResponse<PatternGovernanceResult>>(
    `/api/patterns/${encodeURIComponent(patternId)}/review`,
    { method: 'POST', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function approvePattern(
  patternId: string,
  req: ApprovePatternRequest,
): Promise<PatternGovernanceResult> {
  const res = await apiFetch<ApiResponse<PatternGovernanceResult>>(
    `/api/patterns/${encodeURIComponent(patternId)}/approve`,
    { method: 'POST', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function deprecatePattern(
  patternId: string,
  req: DeprecatePatternRequest,
): Promise<PatternGovernanceResult> {
  const res = await apiFetch<ApiResponse<PatternGovernanceResult>>(
    `/api/patterns/${encodeURIComponent(patternId)}/deprecate`,
    { method: 'POST', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

// ── Pattern version history endpoint (P3-013) ──

export async function getPatternHistory(patternId: string): Promise<PatternVersionHistoryResponse> {
  const res = await apiFetch<ApiResponse<PatternVersionHistoryResponse>>(
    `/api/patterns/${encodeURIComponent(patternId)}/history`,
  );
  return unwrap(res);
}

// ── Pattern usage metrics endpoint (P3-012) ──

export async function getPatternUsage(patternId: string): Promise<PatternUsageMetrics> {
  const res = await apiFetch<ApiResponse<PatternUsageMetrics>>(
    `/api/admin/patterns/${encodeURIComponent(patternId)}/usage`,
  );
  return unwrap(res);
}

// ── Retrieval tuning endpoints (P1-007) ──

export async function getRetrievalSettings(): Promise<RetrievalSettingsResponse> {
  const res = await apiFetch<ApiResponse<RetrievalSettingsResponse>>(
    '/api/admin/retrieval-settings',
  );
  return unwrap(res);
}

export async function updateRetrievalSettings(
  req: UpdateRetrievalSettingsRequest,
): Promise<RetrievalSettingsResponse> {
  const res = await apiFetch<ApiResponse<RetrievalSettingsResponse>>(
    '/api/admin/retrieval-settings',
    { method: 'PUT', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function resetRetrievalSettings(): Promise<void> {
  await apiFetch<ApiResponse<unknown>>('/api/admin/retrieval-settings', {
    method: 'DELETE',
  });
}

// ── Diagnostics endpoints (P1-008) ──

export async function getWebhooksByConnector(
  connectorId: string,
): Promise<WebhookStatusListResponse> {
  const res = await apiFetch<ApiResponse<WebhookStatusListResponse>>(
    `/api/admin/connectors/${connectorId}/webhooks`,
  );
  return unwrap(res);
}

export async function getAllWebhooks(): Promise<WebhookStatusListResponse> {
  const res = await apiFetch<ApiResponse<WebhookStatusListResponse>>(
    '/api/admin/webhooks',
  );
  return unwrap(res);
}

export async function getDiagnosticsSummary(): Promise<DiagnosticsSummaryResponse> {
  const res = await apiFetch<ApiResponse<DiagnosticsSummaryResponse>>(
    '/api/admin/diagnostics/summary',
  );
  return unwrap(res);
}

export async function getDeadLetters(
  maxMessages?: number,
): Promise<DeadLetterListResponse> {
  const params = maxMessages ? `?maxMessages=${maxMessages}` : '';
  const res = await apiFetch<ApiResponse<DeadLetterListResponse>>(
    `/api/admin/ingestion/dead-letters${params}`,
  );
  return unwrap(res);
}

export async function getSloStatus(): Promise<SloStatusResponse> {
  const res = await apiFetch<ApiResponse<SloStatusResponse>>(
    '/api/admin/slo/status',
  );
  return unwrap(res);
}

export async function getSecretsStatus(): Promise<SecretsStatusResponse> {
  return apiFetch<SecretsStatusResponse>('/api/admin/secrets/status');
}

// ── Credential status endpoints (P3-009) ──

export async function getCredentialStatus(): Promise<CredentialStatusSummary> {
  const res = await apiFetch<ApiResponse<CredentialStatusSummary>>(
    '/api/admin/credentials/status',
  );
  return unwrap(res);
}

export async function getConnectorCredentialStatus(
  connectorId: string,
): Promise<ConnectorCredentialStatus> {
  const res = await apiFetch<ApiResponse<ConnectorCredentialStatus>>(
    `/api/admin/connectors/${connectorId}/credential-status`,
  );
  return unwrap(res);
}

export async function rotateConnectorSecret(
  connectorId: string,
  newSecretValue: string,
): Promise<CredentialRotationResult> {
  const res = await apiFetch<ApiResponse<CredentialRotationResult>>(
    `/api/admin/connectors/${connectorId}/rotate-secret`,
    {
      method: 'POST',
      body: JSON.stringify({ newSecretValue }),
    },
  );
  return unwrap(res);
}

// ── Synonym map endpoints (P3-004) ──

export async function listSynonymRules(
  groupName?: string,
): Promise<SynonymRuleListResponse> {
  const params = groupName ? `?groupName=${encodeURIComponent(groupName)}` : '';
  const res = await apiFetch<ApiResponse<SynonymRuleListResponse>>(
    `/api/admin/synonym-rules${params}`,
  );
  return unwrap(res);
}

export async function getSynonymRule(ruleId: string): Promise<SynonymRuleResponse> {
  const res = await apiFetch<ApiResponse<SynonymRuleResponse>>(
    `/api/admin/synonym-rules/${ruleId}`,
  );
  return unwrap(res);
}

export async function createSynonymRule(
  req: CreateSynonymRuleRequest,
): Promise<SynonymRuleResponse> {
  const res = await apiFetch<ApiResponse<SynonymRuleResponse>>(
    '/api/admin/synonym-rules',
    { method: 'POST', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function updateSynonymRule(
  ruleId: string,
  req: UpdateSynonymRuleRequest,
): Promise<SynonymRuleResponse> {
  const res = await apiFetch<ApiResponse<SynonymRuleResponse>>(
    `/api/admin/synonym-rules/${ruleId}`,
    { method: 'PUT', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function deleteSynonymRule(ruleId: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(`/api/admin/synonym-rules/${ruleId}`, {
    method: 'DELETE',
  });
}

export async function syncSynonymMaps(): Promise<SynonymMapSyncResult> {
  const res = await apiFetch<ApiResponse<SynonymMapSyncResult>>(
    '/api/admin/synonym-rules/sync',
    { method: 'POST' },
  );
  return unwrap(res);
}

export async function seedSynonymRules(
  overwriteExisting: boolean = false,
): Promise<{ seeded: number }> {
  const res = await apiFetch<ApiResponse<{ seeded: number }>>(
    '/api/admin/synonym-rules/seed',
    { method: 'POST', body: JSON.stringify({ overwriteExisting }) },
  );
  return unwrap(res);
}

// ── Routing rule endpoints (P1-009) ──

export async function listRoutingRules(): Promise<RoutingRuleListResponse> {
  const res = await apiFetch<ApiResponse<RoutingRuleListResponse>>('/api/admin/routing-rules');
  return unwrap(res);
}

export async function getRoutingRule(ruleId: string): Promise<RoutingRuleDto> {
  const res = await apiFetch<ApiResponse<RoutingRuleDto>>(`/api/admin/routing-rules/${ruleId}`);
  return unwrap(res);
}

export async function createRoutingRule(req: CreateRoutingRuleRequest): Promise<RoutingRuleDto> {
  const res = await apiFetch<ApiResponse<RoutingRuleDto>>('/api/admin/routing-rules', {
    method: 'POST',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function updateRoutingRule(
  ruleId: string,
  req: UpdateRoutingRuleRequest,
): Promise<RoutingRuleDto> {
  const res = await apiFetch<ApiResponse<RoutingRuleDto>>(`/api/admin/routing-rules/${ruleId}`, {
    method: 'PUT',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function deleteRoutingRule(ruleId: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(`/api/admin/routing-rules/${ruleId}`, { method: 'DELETE' });
}

// ── Routing analytics endpoints (P1-009) ──

export async function getRoutingAnalytics(windowDays?: number): Promise<RoutingAnalyticsSummary> {
  const params = windowDays ? `?windowDays=${windowDays}` : '';
  const res = await apiFetch<ApiResponse<RoutingAnalyticsSummary>>(
    `/api/admin/routing/analytics${params}`,
  );
  return unwrap(res);
}

export async function generateRoutingRecommendations(): Promise<RoutingRecommendationListResponse> {
  const res = await apiFetch<ApiResponse<RoutingRecommendationListResponse>>(
    '/api/admin/routing/recommendations/generate',
    { method: 'POST' },
  );
  return unwrap(res);
}

export async function listRoutingRecommendations(
  status?: string,
): Promise<RoutingRecommendationListResponse> {
  const params = status ? `?status=${encodeURIComponent(status)}` : '';
  const res = await apiFetch<ApiResponse<RoutingRecommendationListResponse>>(
    `/api/admin/routing/recommendations${params}`,
  );
  return unwrap(res);
}

export async function applyRoutingRecommendation(
  recommendationId: string,
  req?: ApplyRecommendationRequest,
): Promise<RoutingRecommendationDto> {
  const res = await apiFetch<ApiResponse<RoutingRecommendationDto>>(
    `/api/admin/routing/recommendations/${recommendationId}/apply`,
    { method: 'POST', body: JSON.stringify(req ?? {}) },
  );
  return unwrap(res);
}

export async function dismissRoutingRecommendation(
  recommendationId: string,
): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(
    `/api/admin/routing/recommendations/${recommendationId}/dismiss`,
    { method: 'POST' },
  );
}

// ── Team playbook endpoints (P2-002) ──

export async function listPlaybooks(): Promise<TeamPlaybookListResponse> {
  const res = await apiFetch<ApiResponse<TeamPlaybookListResponse>>('/api/admin/playbooks');
  return unwrap(res);
}

export async function getPlaybook(playbookId: string): Promise<TeamPlaybookDto> {
  const res = await apiFetch<ApiResponse<TeamPlaybookDto>>(
    `/api/admin/playbooks/${playbookId}`,
  );
  return unwrap(res);
}

export async function createPlaybook(req: CreateTeamPlaybookRequest): Promise<TeamPlaybookDto> {
  const res = await apiFetch<ApiResponse<TeamPlaybookDto>>('/api/admin/playbooks', {
    method: 'POST',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function updatePlaybook(
  playbookId: string,
  req: UpdateTeamPlaybookRequest,
): Promise<TeamPlaybookDto> {
  const res = await apiFetch<ApiResponse<TeamPlaybookDto>>(
    `/api/admin/playbooks/${playbookId}`,
    { method: 'PUT', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function deletePlaybook(playbookId: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(`/api/admin/playbooks/${playbookId}`, { method: 'DELETE' });
}

// ── Cost control endpoints (P2-003) ──

export async function getCostSettings(): Promise<CostSettingsResponse> {
  const res = await apiFetch<ApiResponse<CostSettingsResponse>>('/api/admin/cost-settings');
  return unwrap(res);
}

export async function updateCostSettings(
  req: UpdateCostSettingsRequest,
): Promise<CostSettingsResponse> {
  const res = await apiFetch<ApiResponse<CostSettingsResponse>>('/api/admin/cost-settings', {
    method: 'PUT',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function resetCostSettings(): Promise<void> {
  await apiFetch<ApiResponse<unknown>>('/api/admin/cost-settings', { method: 'DELETE' });
}

export async function getTokenUsageSummary(days?: number): Promise<TokenUsageSummary> {
  const params = days ? `?days=${days}` : '';
  const res = await apiFetch<ApiResponse<TokenUsageSummary>>(
    `/api/admin/token-usage/summary${params}`,
  );
  return unwrap(res);
}

export async function getDailyUsage(days?: number): Promise<DailyUsageBreakdown[]> {
  const params = days ? `?days=${days}` : '';
  const res = await apiFetch<ApiResponse<DailyUsageBreakdown[]>>(
    `/api/admin/token-usage/daily${params}`,
  );
  return unwrap(res);
}

export async function getBudgetCheck(): Promise<BudgetCheckResult> {
  const res = await apiFetch<ApiResponse<BudgetCheckResult>>('/api/admin/token-usage/budget-check');
  return unwrap(res);
}

// ── Privacy admin endpoints (P2-001, P2-005) ──

export async function getPiiPolicy(): Promise<PiiPolicyResponse | null> {
  try {
    const res = await apiFetch<ApiResponse<PiiPolicyResponse>>('/api/admin/privacy/pii-policy');
    return unwrap(res);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

export async function updatePiiPolicy(req: PiiPolicyUpdateRequest): Promise<PiiPolicyResponse> {
  const res = await apiFetch<ApiResponse<PiiPolicyResponse>>('/api/admin/privacy/pii-policy', {
    method: 'PUT',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function resetPiiPolicy(): Promise<void> {
  await apiFetch<ApiResponse<unknown>>('/api/admin/privacy/pii-policy', { method: 'DELETE' });
}

export async function getRetentionPolicies(): Promise<RetentionPolicyResponse> {
  const res = await apiFetch<ApiResponse<RetentionPolicyResponse>>('/api/admin/privacy/retention');
  return unwrap(res);
}

export async function updateRetentionPolicy(
  req: RetentionPolicyUpdateRequest,
): Promise<RetentionPolicyEntry> {
  const res = await apiFetch<ApiResponse<RetentionPolicyEntry>>('/api/admin/privacy/retention', {
    method: 'PUT',
    body: JSON.stringify(req),
  });
  return unwrap(res);
}

export async function deleteRetentionPolicy(entityType: string): Promise<void> {
  await apiFetch<ApiResponse<unknown>>(
    `/api/admin/privacy/retention/${encodeURIComponent(entityType)}`,
    { method: 'DELETE' },
  );
}

export async function runRetentionCleanup(): Promise<RetentionCleanupResult[]> {
  const res = await apiFetch<ApiResponse<RetentionCleanupResult[]>>(
    '/api/admin/privacy/retention/cleanup',
    { method: 'POST' },
  );
  return unwrap(res);
}

export async function createDeletionRequest(
  req: DataSubjectDeletionRequest,
): Promise<DataSubjectDeletionResponse> {
  const res = await apiFetch<ApiResponse<DataSubjectDeletionResponse>>(
    '/api/admin/privacy/data-subject-deletion',
    { method: 'POST', body: JSON.stringify(req) },
  );
  return unwrap(res);
}

export async function listDeletionRequests(): Promise<DataSubjectDeletionListResponse> {
  const res = await apiFetch<ApiResponse<DataSubjectDeletionListResponse>>(
    '/api/admin/privacy/data-subject-deletion',
  );
  return unwrap(res);
}

export async function getDeletionRequest(requestId: string): Promise<DataSubjectDeletionResponse> {
  const res = await apiFetch<ApiResponse<DataSubjectDeletionResponse>>(
    `/api/admin/privacy/data-subject-deletion/${requestId}`,
  );
  return unwrap(res);
}

export async function getRetentionHistory(
  entityType?: string,
  skip?: number,
  take?: number,
): Promise<RetentionExecutionHistoryResponse> {
  const params = new URLSearchParams();
  if (entityType) params.set('entityType', entityType);
  if (skip != null) params.set('skip', String(skip));
  if (take != null) params.set('take', String(take));
  const qs = params.toString();
  const res = await apiFetch<ApiResponse<RetentionExecutionHistoryResponse>>(
    `/api/admin/privacy/retention/history${qs ? `?${qs}` : ''}`,
  );
  return unwrap(res);
}

export async function getRetentionCompliance(): Promise<RetentionComplianceReport> {
  const res = await apiFetch<ApiResponse<RetentionComplianceReport>>(
    '/api/admin/privacy/retention/compliance',
  );
  return unwrap(res);
}

// ── Audit & Compliance endpoints ──

export async function queryAuditEvents(
  params?: AuditEventQueryParams,
): Promise<AuditEventListResponse> {
  const qs = new URLSearchParams();
  if (params?.eventType) qs.set('eventType', params.eventType);
  if (params?.actorId) qs.set('actorId', params.actorId);
  if (params?.correlationId) qs.set('correlationId', params.correlationId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  if (params?.page != null) qs.set('page', String(params.page));
  if (params?.pageSize != null) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  const res = await apiFetch<ApiResponse<AuditEventListResponse>>(
    `/api/audit/events${q ? `?${q}` : ''}`,
  );
  return unwrap(res);
}

export async function exportAuditEvents(params?: AuditExportParams): Promise<Blob> {
  const qs = new URLSearchParams();
  if (params?.eventType) qs.set('eventType', params.eventType);
  if (params?.actorId) qs.set('actorId', params.actorId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  const q = qs.toString();

  const headers: Record<string, string> = {};
  if (getAccessToken) {
    const token = await getAccessToken();
    if (token) headers['Authorization'] = `Bearer ${token}`;
  }

  const res = await fetch(`/api/audit/events/export${q ? `?${q}` : ''}`, { headers });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new ApiError(res.status, body || res.statusText);
  }
  return res.blob();
}
