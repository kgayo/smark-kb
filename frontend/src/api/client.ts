import type {
  ApiResponse,
  CreateSessionRequest,
  MessageListResponse,
  SendMessageRequest,
  SessionChatResponse,
  SessionListResponse,
  SessionResponse,
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
