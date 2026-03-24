import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { PatternList } from './PatternList';
import type { PatternSummary } from '../api/types';

function makePattern(overrides: Partial<PatternSummary> = {}): PatternSummary {
  return {
    id: '1',
    patternId: 'pattern-abc',
    title: 'Auth Token Fix',
    problemStatement: 'Token cache invalidation issue',
    trustLevel: 'Draft',
    confidence: 0.65,
    version: 1,
    productArea: 'Auth',
    tags: ['auth', 'cache'],
    supersedesPatternId: null,
    sourceUrl: 'session://test',
    relatedEvidenceCount: 3,
    createdAt: '2026-03-15T10:00:00Z',
    updatedAt: '2026-03-15T10:00:00Z',
    reviewedBy: null,
    reviewedAt: null,
    approvedBy: null,
    approvedAt: null,
    deprecatedBy: null,
    deprecatedAt: null,
    deprecationReason: null,
    ...overrides,
  };
}

describe('PatternList', () => {
  const defaultProps = {
    patterns: [makePattern()],
    onSelect: vi.fn(),
    trustLevelFilter: '' as const,
    onTrustLevelFilterChange: vi.fn(),
    totalCount: 1,
    page: 1,
    hasMore: false,
    onPageChange: vi.fn(),
  };

  it('renders pattern table with data', () => {
    render(<PatternList {...defaultProps} />);
    expect(screen.getByTestId('pattern-table')).toBeTruthy();
    expect(screen.getByText('Auth Token Fix')).toBeTruthy();
    expect(screen.getAllByText('Draft').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('65%')).toBeTruthy();
    expect(screen.getByText('Auth')).toBeTruthy();
    expect(screen.getByLabelText('Open pattern Auth Token Fix')).toBeInTheDocument();
  });

  it('shows empty state when no patterns', () => {
    render(<PatternList {...defaultProps} patterns={[]} totalCount={0} />);
    expect(screen.getByTestId('pattern-empty')).toBeTruthy();
  });

  it('calls onSelect when row clicked', () => {
    const onSelect = vi.fn();
    render(<PatternList {...defaultProps} onSelect={onSelect} />);
    fireEvent.click(screen.getByTestId('pattern-row-pattern-abc'));
    expect(onSelect).toHaveBeenCalledWith('pattern-abc');
  });

  it('renders trust level filter', () => {
    render(<PatternList {...defaultProps} />);
    const filter = screen.getByTestId('trust-filter') as HTMLSelectElement;
    expect(filter).toBeTruthy();
    expect(filter.value).toBe('');
  });

  it('calls onTrustLevelFilterChange when filter changes', () => {
    const onChange = vi.fn();
    render(<PatternList {...defaultProps} onTrustLevelFilterChange={onChange} />);
    fireEvent.change(screen.getByTestId('trust-filter'), { target: { value: 'Approved' } });
    expect(onChange).toHaveBeenCalledWith('Approved');
  });

  it('shows pattern count', () => {
    render(<PatternList {...defaultProps} totalCount={42} />);
    expect(screen.getByTestId('pattern-count').textContent).toBe('42 patterns');
  });

  it('enables next button when hasMore', () => {
    render(<PatternList {...defaultProps} hasMore={true} />);
    const buttons = screen.getByTestId('pattern-pagination').querySelectorAll('button');
    const nextButton = buttons[1];
    expect(nextButton.disabled).toBe(false);
  });

  it('disables previous button on page 1', () => {
    render(<PatternList {...defaultProps} page={1} />);
    const buttons = screen.getByTestId('pattern-pagination').querySelectorAll('button');
    const prevButton = buttons[0];
    expect(prevButton.disabled).toBe(true);
  });

  it('pagination buttons have aria-labels', () => {
    render(<PatternList {...defaultProps} hasMore={true} />);
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next page' })).toBeInTheDocument();
  });
});
