import type {
  ApiResponse,
  CreateEscalationDraftRequest,
  CreateSessionRequest,
  EscalationDraftExportResponse,
  EscalationDraftResponse,
  FeedbackResponse,
  MessageListResponse,
  SendMessageRequest,
  SessionChatResponse,
  SessionListResponse,
  SessionResponse,
  SubmitFeedbackRequest,
  UpdateEscalationDraftRequest,
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
