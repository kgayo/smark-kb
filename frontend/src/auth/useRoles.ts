import { useEffect, useState } from 'react';
import { getMe } from '../api/client';

export function useRoles(): { roles: string[]; loading: boolean } {
  const [roles, setRoles] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    getMe()
      .then((info) => {
        if (!cancelled) setRoles(info.roles);
      })
      .catch((err) => {
        console.warn('[useRoles] Failed to fetch user roles:', err);
        if (!cancelled) setRoles([]);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return { roles, loading };
}

export function hasAdminRole(roles: string[]): boolean {
  return roles.includes('Admin');
}
