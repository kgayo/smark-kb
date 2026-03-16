import { ApiError, setTokenProvider } from './client';

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
