import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { ConnectorResponse, CreateConnectorRequest } from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';
import { ConnectorList } from '../components/ConnectorList';
import { ConnectorDetail } from '../components/ConnectorDetail';
import { CreateConnectorForm } from '../components/CreateConnectorForm';

type View = 'list' | 'detail' | 'create';

export function AdminPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [view, setView] = useState<View>('list');
  const [connectors, setConnectors] = useState<ConnectorResponse[]>([]);
  const [selectedConnector, setSelectedConnector] = useState<ConnectorResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const loadConnectors = useCallback(async () => {
    setLoading(true);
    try {
      const result = await api.listConnectors();
      setConnectors(result.connectors);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load connectors');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (hasAdminRole(roles)) {
      loadConnectors();
    }
  }, [roles, loadConnectors]);

  if (rolesLoading) {
    return (
      <div className="admin-loading" data-testid="admin-loading">
        <p>Loading...</p>
      </div>
    );
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="admin-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to access the connector dashboard.</p>
        <Link to="/" className="btn btn-primary">
          Back to Chat
        </Link>
      </div>
    );
  }

  async function handleSelectConnector(connectorId: string) {
    setError(null);
    try {
      const connector = await api.getConnector(connectorId);
      setSelectedConnector(connector);
      setView('detail');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load connector');
    }
  }

  async function handleCreate(req: CreateConnectorRequest) {
    setCreating(true);
    setCreateError(null);
    try {
      const connector = await api.createConnector(req);
      setConnectors((prev) => [...prev, connector]);
      setSelectedConnector(connector);
      setView('detail');
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Failed to create connector');
      throw e;
    } finally {
      setCreating(false);
    }
  }

  function handleConnectorUpdated(updated: ConnectorResponse) {
    setSelectedConnector(updated);
    setConnectors((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
  }

  function handleConnectorDeleted(connectorId: string) {
    setConnectors((prev) => prev.filter((c) => c.id !== connectorId));
    setSelectedConnector(null);
    setView('list');
  }

  return (
    <div className="admin-layout" data-testid="admin-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Connector Dashboard</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/diagnostics" className="btn btn-sm" data-testid="diagnostics-link">
            Diagnostics
          </Link>
          <Link to="/patterns" className="btn btn-sm" data-testid="patterns-link">
            Patterns
          </Link>
          <Link to="/synonyms" className="btn btn-sm" data-testid="synonyms-link">
            Synonyms
          </Link>
          <Link to="/" className="btn btn-sm" data-testid="back-to-chat">
            Back to Chat
          </Link>
        </div>
      </header>

      {error && (
        <div className="error-banner" role="alert" data-testid="admin-error">
          {error}
        </div>
      )}

      <main className="admin-main">
        {loading && view === 'list' ? (
          <div className="admin-loading">
            <p>Loading connectors...</p>
          </div>
        ) : view === 'create' ? (
          <CreateConnectorForm
            onSubmit={handleCreate}
            onCancel={() => setView('list')}
            submitting={creating}
            error={createError}
          />
        ) : view === 'detail' && selectedConnector ? (
          <ConnectorDetail
            connector={selectedConnector}
            onBack={() => {
              setView('list');
              loadConnectors();
            }}
            onUpdated={handleConnectorUpdated}
            onDeleted={handleConnectorDeleted}
          />
        ) : (
          <ConnectorList
            connectors={connectors}
            onSelect={handleSelectConnector}
            onCreate={() => {
              setCreateError(null);
              setView('create');
            }}
          />
        )}
      </main>
    </div>
  );
}
