import { ApiError, setTokenProvider } from './client';
import * as client from './client';

// ── Helpers ──

function mockFetchSuccess<T>(data: T, wrapped = true) {
  const body = wrapped ? { isSuccess: true, data, error: null } : data;
  return vi.fn().mockResolvedValue({
    ok: true,
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  } as unknown as Response);
}

function mockFetchError(status: number, detail: string) {
  return vi.fn().mockResolvedValue({
    ok: false,
    status,
    statusText: 'Error',
    text: () => Promise.resolve(detail),
  } as unknown as Response);
}

// Reset fetch and token provider between tests
beforeEach(() => {
  vi.restoreAllMocks();
  setTokenProvider(null as unknown as () => Promise<string | null>);
});

// ── ApiError ──

describe('ApiError', () => {
  it('includes status and detail in message', () => {
    const err = new ApiError(404, 'Not found');
    expect(err.status).toBe(404);
    expect(err.detail).toBe('Not found');
    expect(err.message).toBe('API 404: Not found');
    expect(err.name).toBe('ApiError');
  });
});

describe('setTokenProvider', () => {
  it('accepts a token provider function', () => {
    expect(() => setTokenProvider(async () => 'test-token')).not.toThrow();
  });
});

// ── apiFetch internals (via public functions) ──

describe('apiFetch', () => {
  it('includes Authorization header when token provider is set', async () => {
    setTokenProvider(async () => 'my-token');
    const fetchMock = mockFetchSuccess({ sessions: [], totalCount: 0 });
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.listSessions();
    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['Authorization']).toBe('Bearer my-token');
  });

  it('omits Authorization header when token provider returns null', async () => {
    setTokenProvider(async () => null);
    const fetchMock = mockFetchSuccess({ sessions: [], totalCount: 0 });
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.listSessions();
    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['Authorization']).toBeUndefined();
  });

  it('omits Authorization header when no token provider', async () => {
    const fetchMock = mockFetchSuccess({ sessions: [], totalCount: 0 });
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.listSessions();
    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['Authorization']).toBeUndefined();
  });

  it('throws ApiError on non-ok response', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(mockFetchError(500, 'Internal error'));

    await expect(client.listSessions()).rejects.toThrow(ApiError);
    await expect(client.listSessions()).rejects.toThrow('API 500: Internal error');
  });

  it('throws ApiError when unwrap gets isSuccess=false', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ isSuccess: false, data: null, error: 'Validation failed' }),
    } as unknown as Response);

    await expect(client.listSessions()).rejects.toThrow('Validation failed');
  });
});

// ── Session endpoints ──

describe('Session endpoints', () => {
  it('createSession POSTs to /api/sessions', async () => {
    const session = { sessionId: 's1', title: 'New', messageCount: 0 };
    const fetchMock = mockFetchSuccess(session);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.createSession();
    expect(result).toEqual(session);
    expect(fetchMock).toHaveBeenCalledWith('/api/sessions', expect.objectContaining({ method: 'POST' }));
  });

  it('listSessions GETs /api/sessions', async () => {
    const data = { sessions: [], totalCount: 0 };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.listSessions();
    expect(result).toEqual(data);
    expect(fetchMock).toHaveBeenCalledWith('/api/sessions', expect.anything());
  });

  it('deleteSession DELETEs /api/sessions/:id', async () => {
    const fetchMock = mockFetchSuccess(null);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.deleteSession('s1');
    expect(fetchMock).toHaveBeenCalledWith('/api/sessions/s1', expect.objectContaining({ method: 'DELETE' }));
  });

  it('getMessages GETs /api/sessions/:id/messages', async () => {
    const data = { messages: [] };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.getMessages('s1');
    expect(result).toEqual(data);
    expect(fetchMock).toHaveBeenCalledWith('/api/sessions/s1/messages', expect.anything());
  });

  it('sendMessage POSTs to /api/sessions/:id/messages', async () => {
    const data = { session: {}, userMessage: {}, assistantMessage: {}, chatResponse: {} };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.sendMessage('s1', { query: 'hello' });
    expect(result).toEqual(data);
    const [, init] = fetchMock.mock.calls[0];
    expect(JSON.parse(init.body)).toEqual({ query: 'hello' });
  });
});

// ── Escalation draft endpoints ──

describe('Escalation draft endpoints', () => {
  it('createEscalationDraft POSTs to /api/escalations/draft', async () => {
    const draft = { draftId: 'd1', title: 'Esc' };
    const fetchMock = mockFetchSuccess(draft);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.createEscalationDraft({ sessionId: 's1', messageId: 'm1' } as any);
    expect(result).toEqual(draft);
    expect(fetchMock).toHaveBeenCalledWith('/api/escalations/draft', expect.objectContaining({ method: 'POST' }));
  });

  it('exportEscalationDraft GETs /api/escalations/draft/:id/export', async () => {
    const data = { markdown: '# Draft', exportedAt: '2026-03-18' };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.exportEscalationDraft('d1');
    expect(result).toEqual(data);
    expect(fetchMock).toHaveBeenCalledWith('/api/escalations/draft/d1/export', expect.anything());
  });

  it('deleteEscalationDraft DELETEs /api/escalations/draft/:id', async () => {
    const fetchMock = mockFetchSuccess(null);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.deleteEscalationDraft('d1');
    expect(fetchMock).toHaveBeenCalledWith('/api/escalations/draft/d1', expect.objectContaining({ method: 'DELETE' }));
  });
});

// ── Feedback endpoints ──

describe('Feedback endpoints', () => {
  it('submitFeedback POSTs to correct path', async () => {
    const fb = { feedbackId: 'f1', type: 'ThumbsUp', reasonCodes: [] };
    const fetchMock = mockFetchSuccess(fb);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.submitFeedback('s1', 'm1', { type: 'ThumbsUp' } as any);
    expect(result).toEqual(fb);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/sessions/s1/messages/m1/feedback',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('getFeedback returns null on 404', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(mockFetchError(404, 'Not found'));

    const result = await client.getFeedback('s1', 'm1');
    expect(result).toBeNull();
  });

  it('getFeedback rethrows non-404 errors', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(mockFetchError(500, 'Server error'));

    await expect(client.getFeedback('s1', 'm1')).rejects.toThrow(ApiError);
  });
});

// ── Outcome endpoints ──

describe('Outcome endpoints', () => {
  it('recordOutcome POSTs to /api/sessions/:id/outcome', async () => {
    const data = { outcomeId: 'o1', resolutionType: 'Escalated' };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.recordOutcome('s1', { resolutionType: 'Escalated' } as any);
    expect(result).toEqual(data);
  });
});

// ── Connector admin endpoints ──

describe('Connector admin endpoints', () => {
  it('listConnectors GETs /api/admin/connectors', async () => {
    const data = { connectors: [], totalCount: 0 };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.listConnectors();
    expect(result).toEqual(data);
  });

  it('createConnector POSTs to /api/admin/connectors', async () => {
    const conn = { id: 'c1', name: 'Test' };
    const fetchMock = mockFetchSuccess(conn);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.createConnector({ name: 'Test', connectorType: 'AzureDevOps', authType: 'Pat' } as any);
    expect(result).toEqual(conn);
  });

  it('enableConnector POSTs to /api/admin/connectors/:id/enable', async () => {
    const conn = { id: 'c1', status: 'Enabled' };
    const fetchMock = mockFetchSuccess(conn);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.enableConnector('c1');
    expect(result).toEqual(conn);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/connectors/c1/enable',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('disableConnector POSTs to /api/admin/connectors/:id/disable', async () => {
    const conn = { id: 'c1', status: 'Disabled' };
    const fetchMock = mockFetchSuccess(conn);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.disableConnector('c1');
    expect(result).toEqual(conn);
  });

  it('testConnection POSTs to /api/admin/connectors/:id/test', async () => {
    const data = { success: true, message: 'OK' };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.testConnection('c1');
    expect(result).toEqual(data);
  });

  it('syncNow POSTs to /api/admin/connectors/:id/sync-now', async () => {
    const data = { syncRunId: 'sr1', status: 'Pending' };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.syncNow('c1', { isBackfill: true });
    expect(result).toEqual(data);
    const [, init] = fetchMock.mock.calls[0];
    expect(JSON.parse(init.body)).toEqual({ isBackfill: true });
  });

  it('deleteConnector DELETEs /api/admin/connectors/:id', async () => {
    const fetchMock = mockFetchSuccess(null);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.deleteConnector('c1');
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/connectors/c1',
      expect.objectContaining({ method: 'DELETE' }),
    );
  });

  it('listSyncRuns GETs /api/admin/connectors/:id/sync-runs', async () => {
    const data = { syncRuns: [] };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.listSyncRuns('c1');
    expect(result).toEqual(data);
  });
});

// ── Pattern governance endpoints ──

describe('Pattern governance endpoints', () => {
  it('getGovernanceQueue builds query string from params', async () => {
    const data = { patterns: [], totalCount: 0, page: 1, hasMore: false };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.getGovernanceQueue('Draft', 'Billing', 2, 10);
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('trustLevel=Draft');
    expect(url).toContain('productArea=Billing');
    expect(url).toContain('page=2');
    expect(url).toContain('pageSize=10');
  });

  it('getGovernanceQueue omits empty params', async () => {
    const data = { patterns: [], totalCount: 0, page: 1, hasMore: false };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.getGovernanceQueue();
    const [url] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/patterns/governance-queue');
  });

  it('getPatternDetail encodes pattern ID', async () => {
    const data = { patternId: 'p/1', title: 'Test' };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.getPatternDetail('p/1');
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain(encodeURIComponent('p/1'));
  });

  it('reviewPattern POSTs to /api/patterns/:id/review', async () => {
    const data = { success: true };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.reviewPattern('p1', { notes: 'Looks good' });
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/patterns/p1/review',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('approvePattern POSTs to /api/patterns/:id/approve', async () => {
    const data = { success: true };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.approvePattern('p1', {});
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/patterns/p1/approve',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('deprecatePattern POSTs to /api/patterns/:id/deprecate', async () => {
    const data = { success: true };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.deprecatePattern('p1', { reason: 'Outdated' });
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/patterns/p1/deprecate',
      expect.objectContaining({ method: 'POST' }),
    );
  });
});

// ── Retrieval settings endpoints ──

describe('Retrieval settings endpoints', () => {
  it('getRetrievalSettings GETs /api/admin/retrieval-settings', async () => {
    const data = { topK: 20 };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.getRetrievalSettings();
    expect(result).toEqual(data);
  });

  it('updateRetrievalSettings PUTs to /api/admin/retrieval-settings', async () => {
    const data = { topK: 30 };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.updateRetrievalSettings({ topK: 30 } as any);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/retrieval-settings',
      expect.objectContaining({ method: 'PUT' }),
    );
  });

  it('resetRetrievalSettings DELETEs /api/admin/retrieval-settings', async () => {
    const fetchMock = mockFetchSuccess(null);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.resetRetrievalSettings();
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/retrieval-settings',
      expect.objectContaining({ method: 'DELETE' }),
    );
  });
});

// ── Diagnostics endpoints ──

describe('Diagnostics endpoints', () => {
  it('getDiagnosticsSummary GETs /api/admin/diagnostics/summary', async () => {
    const data = { totalConnectors: 3 };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.getDiagnosticsSummary();
    expect(result).toEqual(data);
  });

  it('getDeadLetters passes maxMessages query param', async () => {
    const data = { messages: [] };
    const fetchMock = mockFetchSuccess(data);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    await client.getDeadLetters(5);
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('maxMessages=5');
  });

  it('getSecretsStatus GETs /api/admin/secrets/status (unwrapped)', async () => {
    const data = { secrets: [] };
    const fetchMock = mockFetchSuccess(data, false);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.getSecretsStatus();
    expect(result).toEqual(data);
  });
});

// ── User info endpoint ──

describe('getMe', () => {
  it('GETs /api/me (unwrapped response)', async () => {
    const data = { userId: 'u1', name: 'Test', tenantId: 't1', correlationId: null, roles: ['Admin'] };
    const fetchMock = mockFetchSuccess(data, false);
    vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);

    const result = await client.getMe();
    expect(result).toEqual(data);
    expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything());
  });
});
