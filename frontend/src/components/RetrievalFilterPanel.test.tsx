import { render, screen, fireEvent } from '@testing-library/react';
import { RetrievalFilterPanel } from './RetrievalFilterPanel';
import type { RetrievalFilter } from '../api/types';

describe('RetrievalFilterPanel', () => {
  it('renders filter toggle button', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    expect(screen.getByTestId('filter-toggle')).toBeInTheDocument();
    expect(screen.getByText(/Filters/)).toBeInTheDocument();
  });

  it('filter body is hidden by default', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    expect(screen.queryByTestId('filter-body')).not.toBeInTheDocument();
  });

  it('expands filter body on toggle click', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    expect(screen.getByTestId('filter-body')).toBeInTheDocument();
  });

  it('shows source type chips when expanded', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    expect(screen.getByTestId('filter-source-Ticket')).toBeInTheDocument();
    expect(screen.getByTestId('filter-source-Document')).toBeInTheDocument();
    expect(screen.getByTestId('filter-source-WikiPage')).toBeInTheDocument();
    expect(screen.getByTestId('filter-source-Task')).toBeInTheDocument();
    expect(screen.getByTestId('filter-source-CasePattern')).toBeInTheDocument();
  });

  it('toggles source type on chip click', () => {
    const onChange = vi.fn();
    render(<RetrievalFilterPanel filters={{}} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    fireEvent.click(screen.getByTestId('filter-source-Ticket'));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ sourceTypes: ['Ticket'] }),
    );
  });

  it('removes source type on second click', () => {
    const onChange = vi.fn();
    const filters: RetrievalFilter = { sourceTypes: ['Ticket'] };
    render(<RetrievalFilterPanel filters={filters} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    fireEvent.click(screen.getByTestId('filter-source-Ticket'));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ sourceTypes: undefined }),
    );
  });

  it('shows time horizon select', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    expect(screen.getByTestId('filter-time-horizon')).toBeInTheDocument();
  });

  it('calls onChange with time horizon', () => {
    const onChange = vi.fn();
    render(<RetrievalFilterPanel filters={{}} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    fireEvent.change(screen.getByTestId('filter-time-horizon'), {
      target: { value: '90' },
    });
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ timeHorizonDays: 90 }),
    );
  });

  it('clears time horizon when "Any time" selected', () => {
    const onChange = vi.fn();
    const filters: RetrievalFilter = { timeHorizonDays: 90 };
    render(<RetrievalFilterPanel filters={filters} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    fireEvent.change(screen.getByTestId('filter-time-horizon'), {
      target: { value: '0' },
    });
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ timeHorizonDays: undefined }),
    );
  });

  it('shows clear button only when filters active', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    expect(screen.queryByTestId('filter-clear')).not.toBeInTheDocument();

    // Now with active filters
    const { rerender } = render(
      <RetrievalFilterPanel
        filters={{ sourceTypes: ['Ticket'] }}
        onChange={() => {}}
      />,
    );
    // Need to expand again
    fireEvent.click(screen.getAllByTestId('filter-toggle')[1]);
    expect(screen.getByTestId('filter-clear')).toBeInTheDocument();
  });

  it('clear button resets all filters', () => {
    const onChange = vi.fn();
    const filters: RetrievalFilter = {
      sourceTypes: ['Ticket'],
      timeHorizonDays: 30,
      productAreas: ['Auth'],
    };
    render(<RetrievalFilterPanel filters={filters} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('filter-toggle'));
    fireEvent.click(screen.getByTestId('filter-clear'));
    expect(onChange).toHaveBeenCalledWith({});
  });

  it('shows asterisk indicator when filters are active', () => {
    render(
      <RetrievalFilterPanel
        filters={{ sourceTypes: ['Ticket'] }}
        onChange={() => {}}
      />,
    );
    expect(screen.getByTestId('filter-toggle')).toHaveTextContent('Filters *');
  });

  it('no asterisk when filters empty', () => {
    render(<RetrievalFilterPanel filters={{}} onChange={() => {}} />);
    expect(screen.getByTestId('filter-toggle')).toHaveTextContent('Filters');
    expect(screen.getByTestId('filter-toggle')).not.toHaveTextContent('*');
  });

  it('active source chip has active class', () => {
    render(
      <RetrievalFilterPanel
        filters={{ sourceTypes: ['Ticket'] }}
        onChange={() => {}}
      />,
    );
    fireEvent.click(screen.getByTestId('filter-toggle'));
    expect(screen.getByTestId('filter-source-Ticket')).toHaveClass('active');
    expect(screen.getByTestId('filter-source-Document')).not.toHaveClass('active');
  });
});
