import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PatternDetailView } from './PatternDetailView';
import type { PatternDetail, PatternUsageMetrics } from '../api/types';

vi.mock('../api/client', () => ({
  getPatternUsage: vi.fn(),
  getPatternHistory: vi.fn(),
}));

import { getPatternUsage, getPatternHistory } from '../api/client';
const mockGetPatternUsage = vi.mocked(getPatternUsage);
const mockGetPatternHistory = vi.mocked(getPatternHistory);

function makeDetail(overrides: Partial<PatternDetail> = {}): PatternDetail {
  return {
    id: '1',
    patternId: 'pattern-abc',
    tenantId: 't1',
    title: 'Auth Token Fix',
    problemStatement: 'Token cache invalidation issue',
    rootCause: null,
    symptoms: ['Expired tokens'],
    diagnosisSteps: ['Check logs'],
    resolutionSteps: ['Clear cache', 'Restart service'],
    verificationSteps: ['Verify login'],
    workaround: null,
    escalationCriteria: [],
    escalationTargetTeam: null,
    relatedEvidenceIds: ['ev1', 'ev2'],
    confidence: 0.65,
    trustLevel: 'Draft',
    version: 1,
    supersedesPatternId: null,
    applicabilityConstraints: [],
    exclusions: [],
    productArea: 'Auth',
    tags: ['auth'],
    visibility: 'Internal',
    accessLabel: 'Internal',
    sourceUrl: 'session://test',
    createdAt: '2026-03-15T10:00:00Z',
    updatedAt: '2026-03-15T10:00:00Z',
    reviewedBy: null,
    reviewedAt: null,
    reviewNotes: null,
    approvedBy: null,
    approvedAt: null,
    approvalNotes: null,
    deprecatedBy: null,
    deprecatedAt: null,
    deprecationReason: null,
    ...overrides,
  };
}

describe('PatternDetailView', () => {
  beforeEach(() => {
    mockGetPatternUsage.mockResolvedValue({
      patternId: 'pattern-abc',
      totalCitations: 0,
      citationsLast7Days: 0,
      citationsLast30Days: 0,
      citationsLast90Days: 0,
      uniqueUsers: 0,
      averageConfidence: 0,
      lastCitedAt: null,
      firstCitedAt: null,
      dailyBreakdown: [],
    });
    mockGetPatternHistory.mockResolvedValue({
      patternId: 'pattern-abc',
      entries: [],
      totalCount: 0,
    });
  });

  const defaultProps = {
    pattern: makeDetail(),
    onBack: vi.fn(),
    onReview: vi.fn().mockResolvedValue(undefined),
    onApprove: vi.fn().mockResolvedValue(undefined),
    onDeprecate: vi.fn().mockResolvedValue(undefined),
    actionLoading: false,
  };

  it('renders pattern title and content', () => {
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.getByText('Auth Token Fix')).toBeTruthy();
    expect(screen.getByTestId('problem-statement').textContent).toBe('Token cache invalidation issue');
    expect(screen.getByTestId('pattern-id').textContent).toBe('pattern-abc');
  });

  it('shows review and approve buttons for Draft pattern', () => {
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.getByTestId('btn-review')).toBeTruthy();
    expect(screen.getByTestId('btn-approve')).toBeTruthy();
    expect(screen.getByTestId('btn-show-deprecate')).toBeTruthy();
  });

  it('hides review button for Reviewed pattern', () => {
    render(<PatternDetailView {...defaultProps} pattern={makeDetail({ trustLevel: 'Reviewed' })} />);
    expect(screen.queryByTestId('btn-review')).toBeNull();
    expect(screen.getByTestId('btn-approve')).toBeTruthy();
  });

  it('hides approve and review buttons for Approved pattern', () => {
    render(<PatternDetailView {...defaultProps} pattern={makeDetail({ trustLevel: 'Approved' })} />);
    expect(screen.queryByTestId('btn-review')).toBeNull();
    expect(screen.queryByTestId('btn-approve')).toBeNull();
    expect(screen.getByTestId('btn-show-deprecate')).toBeTruthy();
  });

  it('hides all action buttons for Deprecated pattern', () => {
    render(<PatternDetailView {...defaultProps} pattern={makeDetail({ trustLevel: 'Deprecated' })} />);
    expect(screen.queryByTestId('btn-review')).toBeNull();
    expect(screen.queryByTestId('btn-approve')).toBeNull();
    expect(screen.queryByTestId('btn-show-deprecate')).toBeNull();
  });

  it('calls onApprove when approve button clicked', async () => {
    const onApprove = vi.fn().mockResolvedValue(undefined);
    render(<PatternDetailView {...defaultProps} onApprove={onApprove} />);
    fireEvent.click(screen.getByTestId('btn-approve'));
    await waitFor(() => expect(onApprove).toHaveBeenCalledWith(''));
  });

  it('calls onReview when review button clicked', async () => {
    const onReview = vi.fn().mockResolvedValue(undefined);
    render(<PatternDetailView {...defaultProps} onReview={onReview} />);
    fireEvent.click(screen.getByTestId('btn-review'));
    await waitFor(() => expect(onReview).toHaveBeenCalledWith(''));
  });

  it('shows deprecate form when deprecate button clicked', () => {
    render(<PatternDetailView {...defaultProps} />);
    fireEvent.click(screen.getByTestId('btn-show-deprecate'));
    expect(screen.getByTestId('deprecate-form')).toBeTruthy();
  });

  it('calls onDeprecate with reason', async () => {
    const onDeprecate = vi.fn().mockResolvedValue(undefined);
    render(<PatternDetailView {...defaultProps} onDeprecate={onDeprecate} />);
    fireEvent.click(screen.getByTestId('btn-show-deprecate'));
    fireEvent.change(screen.getByTestId('deprecate-reason'), { target: { value: 'Outdated' } });
    fireEvent.click(screen.getByTestId('btn-confirm-deprecate'));
    await waitFor(() => expect(onDeprecate).toHaveBeenCalledWith('Outdated', undefined));
  });

  it('renders governance history when present', () => {
    const pattern = makeDetail({
      reviewedBy: 'user-1',
      reviewedAt: '2026-03-16T10:00:00Z',
      reviewNotes: 'Looks good',
      approvedBy: 'lead-1',
      approvedAt: '2026-03-17T10:00:00Z',
    });
    render(<PatternDetailView {...defaultProps} pattern={pattern} />);
    expect(screen.getByTestId('governance-history')).toBeTruthy();
    expect(screen.getByText(/user-1/)).toBeTruthy();
    expect(screen.getByText(/lead-1/)).toBeTruthy();
    expect(screen.getByText('Looks good')).toBeTruthy();
  });

  it('renders resolution steps', () => {
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.getByTestId('resolution-steps')).toBeTruthy();
    expect(screen.getByText('Clear cache')).toBeTruthy();
    expect(screen.getByText('Restart service')).toBeTruthy();
  });

  it('renders root cause when present', () => {
    const pattern = makeDetail({ rootCause: 'Memory leak in token cache' });
    render(<PatternDetailView {...defaultProps} pattern={pattern} />);
    expect(screen.getByTestId('root-cause').textContent).toBe('Memory leak in token cache');
    expect(screen.getByText('Root Cause')).toBeTruthy();
  });

  it('hides root cause section when null', () => {
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.queryByTestId('root-cause')).toBeNull();
  });

  it('calls onBack when back button clicked', () => {
    const onBack = vi.fn();
    render(<PatternDetailView {...defaultProps} onBack={onBack} />);
    fireEvent.click(screen.getByTestId('pattern-back'));
    expect(onBack).toHaveBeenCalled();
  });

  // ── Usage metrics tests (P3-012) ──

  function makeUsage(overrides: Partial<PatternUsageMetrics> = {}): PatternUsageMetrics {
    return {
      patternId: 'pattern-abc',
      totalCitations: 15,
      citationsLast7Days: 3,
      citationsLast30Days: 10,
      citationsLast90Days: 15,
      uniqueUsers: 5,
      averageConfidence: 0.82,
      lastCitedAt: '2026-03-18T10:00:00Z',
      firstCitedAt: '2026-01-15T08:00:00Z',
      dailyBreakdown: Array.from({ length: 30 }, (_, i) => ({
        date: `2026-02-${String(18 + Math.floor(i / 28)).padStart(2, '0')}-${String((i % 28) + 1).padStart(2, '0')}`,
        citations: i === 28 ? 2 : 0,
      })),
      ...overrides,
    };
  }

  it('renders usage metrics when loaded', async () => {
    mockGetPatternUsage.mockResolvedValue(makeUsage());
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('usage-total')).toBeTruthy());
    expect(screen.getByTestId('usage-total').textContent).toBe('15');
    expect(screen.getByTestId('usage-7d').textContent).toBe('3');
    expect(screen.getByTestId('usage-30d').textContent).toBe('10');
    expect(screen.getByTestId('usage-90d').textContent).toBe('15');
    expect(screen.getByTestId('usage-users').textContent).toBe('5');
    expect(screen.getByTestId('usage-confidence').textContent).toBe('82%');
  });

  it('shows loading state for usage metrics', () => {
    mockGetPatternUsage.mockReturnValue(new Promise(() => {})); // never resolves
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.getByText('Loading usage data...')).toBeTruthy();
  });

  it('shows unavailable message when usage fetch fails', async () => {
    mockGetPatternUsage.mockRejectedValue(new Error('Network error'));
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('usage-unavailable')).toBeTruthy());
    expect(screen.getByText('Usage data unavailable.')).toBeTruthy();
  });

  it('shows Never for last cited when no citations', async () => {
    mockGetPatternUsage.mockResolvedValue(makeUsage({
      totalCitations: 0,
      lastCitedAt: null,
      firstCitedAt: null,
      averageConfidence: 0,
    }));
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('usage-last-cited')).toBeTruthy());
    expect(screen.getByTestId('usage-last-cited').textContent).toBe('Never');
    expect(screen.getByTestId('usage-confidence').textContent).toBe('—');
  });

  it('renders daily breakdown bar chart when data has citations', async () => {
    mockGetPatternUsage.mockResolvedValue(makeUsage({
      dailyBreakdown: Array.from({ length: 30 }, (_, i) => ({
        date: `2026-03-${String(i + 1).padStart(2, '0')}`,
        citations: i === 5 ? 3 : i === 10 ? 1 : 0,
      })),
    }));
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('usage-daily')).toBeTruthy());
    expect(screen.getByText('Daily Citations (Last 30 Days)')).toBeTruthy();
  });

  // ── Version history tests (P3-013) ──

  it('shows no history message when no version history entries', async () => {
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('no-history')).toBeTruthy());
    expect(screen.getByText('No version history recorded.')).toBeTruthy();
  });

  it('renders version history table when entries exist', async () => {
    mockGetPatternHistory.mockResolvedValue({
      patternId: 'pattern-abc',
      entries: [
        {
          id: 'h1',
          patternId: 'pattern-abc',
          version: 1,
          changedBy: 'lead-1',
          changedAt: '2026-03-17T10:00:00Z',
          changedFields: ['TrustLevel', 'ReviewedBy', 'ReviewNotes'],
          previousValues: { TrustLevel: 'Draft', ReviewedBy: null, ReviewNotes: null },
          changeType: 'trust_transition',
          summary: 'Draft → Reviewed',
        },
      ],
      totalCount: 1,
    });
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('history-table')).toBeTruthy());
    expect(screen.getByText('Draft → Reviewed')).toBeTruthy();
    expect(screen.getByText('lead-1')).toBeTruthy();
    expect(screen.getByText('TrustLevel, ReviewedBy, ReviewNotes')).toBeTruthy();
  });

  it('shows loading state for version history', () => {
    mockGetPatternHistory.mockReturnValue(new Promise(() => {})); // never resolves
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.getByText('Loading history...')).toBeTruthy();
  });

  it('shows no history message when history fetch fails', async () => {
    mockGetPatternHistory.mockRejectedValue(new Error('Network error'));
    render(<PatternDetailView {...defaultProps} />);
    await waitFor(() => expect(screen.getByTestId('no-history')).toBeTruthy());
  });

  it('has aria-labels on governance action buttons and inputs', () => {
    render(<PatternDetailView {...defaultProps} />);
    expect(screen.getByLabelText('Back to pattern list')).toBeTruthy();
    expect(screen.getByLabelText('Mark pattern as reviewed')).toBeTruthy();
    expect(screen.getByLabelText('Approve pattern')).toBeTruthy();
    expect(screen.getByLabelText('Deprecate pattern')).toBeTruthy();
    expect(screen.getByLabelText('Governance action notes')).toBeTruthy();
  });

  it('has aria-labels on deprecation form inputs', () => {
    render(<PatternDetailView {...defaultProps} />);
    fireEvent.click(screen.getByTestId('btn-show-deprecate'));
    expect(screen.getByLabelText('Deprecation reason')).toBeTruthy();
    expect(screen.getByLabelText('Superseding pattern ID')).toBeTruthy();
    expect(screen.getByLabelText('Cancel deprecation')).toBeTruthy();
    expect(screen.getByLabelText('Confirm pattern deprecation')).toBeTruthy();
  });
});
