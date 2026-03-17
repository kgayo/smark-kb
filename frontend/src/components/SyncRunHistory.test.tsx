import { render, screen } from '@testing-library/react';
import { SyncRunHistory } from './SyncRunHistory';
import type { SyncRunSummary } from '../api/types';

const mockRun: SyncRunSummary = {
  id: 'run-1',
  status: 'Completed',
  isBackfill: false,
  startedAt: '2026-03-15T11:00:00Z',
  completedAt: '2026-03-15T11:05:00Z',
  recordsProcessed: 42,
  recordsFailed: 0,
  errorDetail: null,
};

const failedRun: SyncRunSummary = {
  id: 'run-2',
  status: 'Failed',
  isBackfill: true,
  startedAt: '2026-03-14T10:00:00Z',
  completedAt: '2026-03-14T10:01:00Z',
  recordsProcessed: 10,
  recordsFailed: 5,
  errorDetail: 'Auth token expired',
};

describe('SyncRunHistory', () => {
  it('shows loading state', () => {
    render(<SyncRunHistory syncRuns={[]} loading={true} />);
    expect(screen.getByText(/Loading sync history/)).toBeInTheDocument();
  });

  it('shows empty state', () => {
    render(<SyncRunHistory syncRuns={[]} loading={false} />);
    expect(screen.getByText('No sync runs yet.')).toBeInTheDocument();
  });

  it('renders sync run table', () => {
    render(<SyncRunHistory syncRuns={[mockRun]} loading={false} />);
    expect(screen.getByTestId('sync-table')).toBeInTheDocument();
    expect(screen.getByText('Completed')).toBeInTheDocument();
    expect(screen.getByText('Incremental')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it('shows backfill label', () => {
    render(<SyncRunHistory syncRuns={[failedRun]} loading={false} />);
    expect(screen.getByText('Backfill')).toBeInTheDocument();
  });

  it('shows error detail for failed runs', () => {
    render(<SyncRunHistory syncRuns={[failedRun]} loading={false} />);
    expect(screen.getByText('Auth token expired')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
  });

  it('renders multiple runs', () => {
    render(<SyncRunHistory syncRuns={[mockRun, failedRun]} loading={false} />);
    expect(screen.getByTestId('sync-row-run-1')).toBeInTheDocument();
    expect(screen.getByTestId('sync-row-run-2')).toBeInTheDocument();
  });
});
