import type { ConnectorResponse } from '../api/types';
import { ConnectorStatuses, SyncRunStatuses } from '../constants/enums';
import { formatDateTimeLocale } from '../utils/dateFormat';
import { syncStatusClass } from '../utils/cssClassHelpers';

const formatDate = formatDateTimeLocale;

function statusClass(status: string): string {
  return status === ConnectorStatuses.Enabled ? 'status-enabled' : 'status-disabled';
}

interface ConnectorListProps {
  connectors: ConnectorResponse[];
  onSelect: (connectorId: string) => void;
  onCreate: () => void;
}

export function ConnectorList({ connectors, onSelect, onCreate }: ConnectorListProps) {
  return (
    <div className="connector-list" data-testid="connector-list">
      <div className="connector-list-header">
        <h2>Connectors</h2>
        <button className="btn btn-primary" onClick={onCreate} data-testid="create-connector-btn" aria-label="Create new connector">
          + New Connector
        </button>
      </div>

      {connectors.length === 0 ? (
        <div className="connector-empty" data-testid="connector-empty">
          <p>No connectors configured yet.</p>
          <p>Create a connector to start ingesting knowledge sources.</p>
        </div>
      ) : (
        <table className="connector-table" data-testid="connector-table" aria-label="Configured connectors">
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Status</th>
              <th>Auth</th>
              <th>Last Sync</th>
              <th>Updated</th>
            </tr>
          </thead>
          <tbody>
            {connectors.map((c) => (
              <tr
                key={c.id}
                className="connector-row"
                onClick={() => onSelect(c.id)}
                data-testid={`connector-row-${c.id}`}
                aria-label={`Open connector ${c.name}`}
              >
                <td className="connector-name">{c.name}</td>
                <td>{c.connectorType}</td>
                <td>
                  <span className={`connector-status ${statusClass(c.status)}`}>
                    {c.status}
                  </span>
                </td>
                <td>{c.authType}</td>
                <td>
                  {c.lastSyncRun ? (
                    <span className={`sync-status ${syncStatusClass(c.lastSyncRun.status)}`}>
                      {c.lastSyncRun.status}
                      {c.lastSyncRun.status === SyncRunStatuses.Completed &&
                        ` (${c.lastSyncRun.recordsProcessed} records)`}
                      {c.lastSyncRun.status === SyncRunStatuses.Failed && c.lastSyncRun.errorDetail &&
                        ` - ${c.lastSyncRun.errorDetail.slice(0, 40)}...`}
                    </span>
                  ) : (
                    <span className="sync-status sync-status-none">Never</span>
                  )}
                </td>
                <td className="connector-date">{formatDate(c.updatedAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
