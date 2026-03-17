import type {
  ApiResponse,
  ApproveEscalationDraftRequest,
  ApprovePatternRequest,
  ConnectorListResponse,
  ConnectorResponse,
  ConnectorValidationResult,
  CreateConnectorRequest,
  CreateEscalationDraftRequest,
  CreateSessionRequest,
  DeprecatePatternRequest,
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
  RecordOutcomeRequest,
  RetrievalSettingsResponse,
  ReviewPatternRequest,
  SendMessageRequest,
  SessionChatResponse,
  SessionListResponse,
  SessionResponse,
  SubmitFeedbackRequest,
  SyncNowRequest,
  SyncRunListResponse,
  SyncRunSummary,
  TestConnectionResponse,
  UpdateConnectorRequest,
  UpdateEscalationDraftRequest,
  UpdateRetrievalSettingsRequest,
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
