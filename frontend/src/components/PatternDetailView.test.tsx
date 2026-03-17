import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PatternDetailView } from './PatternDetailView';
import type { PatternDetail } from '../api/types';

function makeDetail(overrides: Partial<PatternDetail> = {}): PatternDetail {
  return {
    id: '1',
    patternId: 'pattern-abc',
    tenantId: 't1',
    title: 'Auth Token Fix',
    problemStatement: 'Token cache invalidation issue',
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

  it('calls onBack when back button clicked', () => {
    const onBack = vi.fn();
    render(<PatternDetailView {...defaultProps} onBack={onBack} />);
    fireEvent.click(screen.getByTestId('pattern-back'));
    expect(onBack).toHaveBeenCalled();
  });
});
