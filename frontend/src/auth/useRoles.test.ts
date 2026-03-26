import { renderHook, waitFor } from '@testing-library/react';
import { useRoles, hasAdminRole } from './useRoles';
import { AppRoles } from './roles';
import * as client from '../api/client';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
}));

const mockedGetMe = vi.mocked(client.getMe);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useRoles', () => {
  it('starts with loading=true and empty roles', () => {
    mockedGetMe.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useRoles());
    expect(result.current.loading).toBe(true);
    expect(result.current.roles).toEqual([]);
  });

  it('sets roles after successful fetch', async () => {
    mockedGetMe.mockResolvedValue({
      userId: 'u1',
      name: 'User',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.Admin, AppRoles.SupportAgent],
    });

    const { result } = renderHook(() => useRoles());

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });
    expect(result.current.roles).toEqual([AppRoles.Admin, AppRoles.SupportAgent]);
  });

  it('sets empty roles on fetch failure', async () => {
    mockedGetMe.mockRejectedValue(new Error('Unauthorized'));

    const { result } = renderHook(() => useRoles());

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });
    expect(result.current.roles).toEqual([]);
  });

  it('cleans up on unmount (cancelled flag)', async () => {
    let resolveMe: (value: any) => void;
    mockedGetMe.mockReturnValue(
      new Promise((resolve) => {
        resolveMe = resolve;
      }),
    );

    const { result, unmount } = renderHook(() => useRoles());
    expect(result.current.loading).toBe(true);

    unmount();

    // Resolve after unmount - should not throw
    resolveMe!({
      userId: 'u1',
      name: 'User',
      tenantId: 't1',
      correlationId: null,
      roles: [AppRoles.Admin],
    });
  });
});

describe('hasAdminRole', () => {
  it('returns true when roles include Admin', () => {
    expect(hasAdminRole([AppRoles.SupportAgent, AppRoles.Admin])).toBe(true);
  });

  it('returns false when roles do not include Admin', () => {
    expect(hasAdminRole([AppRoles.SupportAgent, AppRoles.SupportLead])).toBe(false);
  });

  it('returns false for empty roles', () => {
    expect(hasAdminRole([])).toBe(false);
  });
});
