import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { logger } from '../utils/logger';
import type { PatternDetail, PatternSummary, TrustLevel } from '../api/types';
import * as api from '../api/client';
import { useRoles } from '../auth/useRoles';
import { AppRoles } from '../auth/roles';
import { PatternList } from '../components/PatternList';
import { PatternDetailView } from '../components/PatternDetailView';

function hasGovernanceRole(roles: string[]): boolean {
  return roles.some(r => r === AppRoles.Admin || r === AppRoles.SupportLead);
}

export function PatternGovernancePage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [patterns, setPatterns] = useState<PatternSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [trustFilter, setTrustFilter] = useState<TrustLevel | ''>('');
  const [selectedPattern, setSelectedPattern] = useState<PatternDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadPatterns = useCallback(async (p: number, trust: TrustLevel | '') => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.getGovernanceQueue(trust || undefined, undefined, p, 20);
      setPatterns(result.patterns);
      setTotalCount(result.totalCount);
      setPage(result.page);
      setHasMore(result.hasMore);
    } catch (e) {
      logger.warn('[PatternGovernancePage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load patterns');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (hasGovernanceRole(roles)) {
      loadPatterns(page, trustFilter);
    }
  }, [roles, loadPatterns, page, trustFilter]);

  if (rolesLoading) {
    return (
      <div className="admin-loading" data-testid="governance-loading">
        <p>Loading...</p>
      </div>
    );
  }

  if (!hasGovernanceRole(roles)) {
    return (
      <div className="admin-denied" data-testid="governance-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin or SupportLead role to access pattern governance.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  async function handleSelectPattern(patternId: string) {
    setError(null);
    try {
      const detail = await api.getPatternDetail(patternId);
      setSelectedPattern(detail);
    } catch (e) {
      logger.warn('[PatternGovernancePage]', e);
      setError(e instanceof Error ? e.message : 'Failed to load pattern');
    }
  }

  async function handleReview(notes: string) {
    if (!selectedPattern) return;
    setActionLoading(true);
    setError(null);
    try {
      await api.reviewPattern(selectedPattern.patternId, { notes: notes || undefined });
      const updated = await api.getPatternDetail(selectedPattern.patternId);
      setSelectedPattern(updated);
      await loadPatterns(page, trustFilter);
    } catch (e) {
      logger.warn('[PatternGovernancePage]', e);
      setError(e instanceof Error ? e.message : 'Failed to review pattern');
    } finally {
      setActionLoading(false);
    }
  }

  async function handleApprove(notes: string) {
    if (!selectedPattern) return;
    setActionLoading(true);
    setError(null);
    try {
      await api.approvePattern(selectedPattern.patternId, { notes: notes || undefined });
      const updated = await api.getPatternDetail(selectedPattern.patternId);
      setSelectedPattern(updated);
      await loadPatterns(page, trustFilter);
    } catch (e) {
      logger.warn('[PatternGovernancePage]', e);
      setError(e instanceof Error ? e.message : 'Failed to approve pattern');
    } finally {
      setActionLoading(false);
    }
  }

  async function handleDeprecate(reason: string, supersedingPatternId?: string) {
    if (!selectedPattern) return;
    setActionLoading(true);
    setError(null);
    try {
      await api.deprecatePattern(selectedPattern.patternId, {
        reason: reason || undefined,
        supersedingPatternId,
      });
      const updated = await api.getPatternDetail(selectedPattern.patternId);
      setSelectedPattern(updated);
      await loadPatterns(page, trustFilter);
    } catch (e) {
      logger.warn('[PatternGovernancePage]', e);
      setError(e instanceof Error ? e.message : 'Failed to deprecate pattern');
    } finally {
      setActionLoading(false);
    }
  }

  return (
    <div className="admin-layout" data-testid="governance-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Pattern Governance</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/" className="btn btn-sm" data-testid="back-to-chat">Back to Chat</Link>
        </div>
      </header>

      {error && (
        <div className="error-banner" role="alert" data-testid="governance-error">
          {error}
        </div>
      )}

      <main className="admin-main">
        {selectedPattern ? (
          <PatternDetailView
            pattern={selectedPattern}
            onBack={() => setSelectedPattern(null)}
            onReview={handleReview}
            onApprove={handleApprove}
            onDeprecate={handleDeprecate}
            actionLoading={actionLoading}
          />
        ) : loading ? (
          <div className="admin-loading">
            <p>Loading patterns...</p>
          </div>
        ) : (
          <PatternList
            patterns={patterns}
            onSelect={handleSelectPattern}
            selectedPatternId={undefined}
            trustLevelFilter={trustFilter}
            onTrustLevelFilterChange={(level) => {
              setTrustFilter(level);
              setPage(1);
            }}
            totalCount={totalCount}
            page={page}
            hasMore={hasMore}
            onPageChange={setPage}
          />
        )}
      </main>
    </div>
  );
}
