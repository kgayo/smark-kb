import type { SyncRunSummary } from '../api/types';
import { formatDateTimeLocale } from '../utils/dateFormat';
import { syncStatusClass } from '../utils/cssClassHelpers';

const formatDate = formatDateTimeLocale;

function durationStr(start: string, end: string | null): string {
  if (!end) return 'In progress';
  const ms = new Date(end).getTime() - new Date(start).getTime();
  const secs = Math.floor(ms / 1000);
  if (secs < 60) return `${secs}s`;
  const mins = Math.floor(secs / 60);
  const remSecs = secs % 60;
  return `${mins}m ${remSecs}s`;
}

const statusClass = syncStatusClass;

interface SyncRunHistoryProps {
  syncRuns: SyncRunSummary[];
  loading: boolean;
}

export function SyncRunHistory({ syncRuns, loading }: SyncRunHistoryProps) {
  if (loading) {
    return <p className="sync-loading">Loading sync history...</p>;
  }

  return (
    <div className="sync-run-history" data-testid="sync-run-history">
      <h4>Sync History</h4>
      {syncRuns.length === 0 ? (
        <p className="sync-empty">No sync runs yet.</p>
      ) : (
        <table className="sync-table" data-testid="sync-table" aria-label="Sync run history">
          <thead>
            <tr>
              <th>Status</th>
              <th>Type</th>
              <th>Started</th>
              <th>Duration</th>
              <th>Records</th>
              <th>Failed</th>
              <th>Error</th>
            </tr>
          </thead>
          <tbody>
            {syncRuns.map((run) => (
              <tr key={run.id} data-testid={`sync-row-${run.id}`}>
                <td>
                  <span className={`sync-status ${statusClass(run.status)}`}>
                    {run.status}
                  </span>
                </td>
                <td>{run.isBackfill ? 'Backfill' : 'Incremental'}</td>
                <td className="connector-date">{formatDate(run.startedAt)}</td>
                <td>{durationStr(run.startedAt, run.completedAt)}</td>
                <td>{run.recordsProcessed}</td>
                <td>{run.recordsFailed > 0 ? run.recordsFailed : '-'}</td>
                <td className="sync-error-cell">
                  {run.errorDetail ? (
                    <span title={run.errorDetail}>
                      {run.errorDetail.length > 60
                        ? `${run.errorDetail.slice(0, 60)}...`
                        : run.errorDetail}
                    </span>
                  ) : (
                    '-'
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
