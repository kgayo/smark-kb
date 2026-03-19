import { render, screen } from '@testing-library/react';
import { ConfidenceBadge } from './ConfidenceBadge';

describe('ConfidenceBadge', () => {
  it('renders high confidence with green styling', () => {
    render(<ConfidenceBadge confidence={0.85} label="High" />);
    const badge = screen.getByTestId('confidence-badge');
    expect(badge).toHaveTextContent('High (85%)');
    expect(badge).toHaveClass('confidence-high');
  });

  it('renders medium confidence with yellow styling', () => {
    render(<ConfidenceBadge confidence={0.55} label="Medium" />);
    const badge = screen.getByTestId('confidence-badge');
    expect(badge).toHaveTextContent('Medium (55%)');
    expect(badge).toHaveClass('confidence-medium');
  });

  it('renders low confidence with red styling', () => {
    render(<ConfidenceBadge confidence={0.2} label="Low" />);
    const badge = screen.getByTestId('confidence-badge');
    expect(badge).toHaveTextContent('Low (20%)');
    expect(badge).toHaveClass('confidence-low');
  });

  it('rounds confidence percentage', () => {
    render(<ConfidenceBadge confidence={0.777} label="High" />);
    expect(screen.getByTestId('confidence-badge')).toHaveTextContent('High (78%)');
  });

  it('includes tooltip with full details', () => {
    render(<ConfidenceBadge confidence={0.65} label="Medium" />);
    expect(screen.getByTestId('confidence-badge')).toHaveAttribute(
      'title',
      'Confidence: 65% (Medium)',
    );
  });

  it('renders rationale text when provided', () => {
    render(
      <ConfidenceBadge
        confidence={0.85}
        label="High"
        rationale="3 evidence chunks matched with high relevance."
      />,
    );
    const rationale = screen.getByTestId('confidence-rationale');
    expect(rationale).toHaveTextContent('3 evidence chunks matched with high relevance.');
  });

  it('does not render rationale when not provided', () => {
    render(<ConfidenceBadge confidence={0.5} label="Medium" />);
    expect(screen.queryByTestId('confidence-rationale')).not.toBeInTheDocument();
  });

  it('does not render rationale when null', () => {
    render(<ConfidenceBadge confidence={0.5} label="Medium" rationale={null} />);
    expect(screen.queryByTestId('confidence-rationale')).not.toBeInTheDocument();
  });

  it('includes rationale in tooltip when provided', () => {
    render(
      <ConfidenceBadge
        confidence={0.65}
        label="Medium"
        rationale="2 chunks, moderate relevance."
      />,
    );
    expect(screen.getByTestId('confidence-badge')).toHaveAttribute(
      'title',
      'Confidence: 65% (Medium) — 2 chunks, moderate relevance.',
    );
  });
});
