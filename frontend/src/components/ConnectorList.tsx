import type { ConnectorResponse } from '../api/types';

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

function statusClass(status: string): string {
  return status === 'Enabled' ? 'status-enabled' : 'status-disabled';
}

function syncStatusClass(status: string): string {
  switch (status) {
    case 'Completed':
      return 'sync-status-completed';
    case 'Failed':
      return 'sync-status-failed';
    case 'Running':
      return 'sync-status-running';
    default:
      return 'sync-status-pending';
  }
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
        <button className="btn btn-primary" onClick={onCreate} data-testid="create-connector-btn">
          + New Connector
        </button>
      </div>

      {connectors.length === 0 ? (
        <div className="connector-empty" data-testid="connector-empty">
          <p>No connectors configured yet.</p>
          <p>Create a connector to start ingesting knowledge sources.</p>
        </div>
      ) : (
        <table className="connector-table" data-testid="connector-table">
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
                      {c.lastSyncRun.status === 'Completed' &&
                        ` (${c.lastSyncRun.recordsProcessed} records)`}
                      {c.lastSyncRun.status === 'Failed' && c.lastSyncRun.errorDetail &&
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
