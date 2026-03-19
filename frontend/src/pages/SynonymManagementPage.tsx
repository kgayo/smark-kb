import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  SynonymRuleResponse,
  SynonymRuleListResponse,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

export function SynonymManagementPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [rules, setRules] = useState<SynonymRuleResponse[]>([]);
  const [groups, setGroups] = useState<string[]>([]);
  const [selectedGroup, setSelectedGroup] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);

  // Create form state
  const [showCreate, setShowCreate] = useState(false);
  const [newRule, setNewRule] = useState('');
  const [newGroup, setNewGroup] = useState('general');
  const [newDescription, setNewDescription] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);

  // Edit state
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editRule, setEditRule] = useState('');
  const [editGroup, setEditGroup] = useState('');
  const [editDescription, setEditDescription] = useState('');

  const loadRules = useCallback(async (group?: string) => {
    setLoading(true);
    setError(null);
    try {
      const result: SynonymRuleListResponse = await api.listSynonymRules(group || undefined);
      setRules(result.rules);
      setGroups(result.groups);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load synonym rules.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!rolesLoading && hasAdminRole(roles)) {
      loadRules(selectedGroup);
    }
  }, [rolesLoading, roles, loadRules, selectedGroup]);

  if (rolesLoading) return <div style={{ padding: 32 }}>Loading...</div>;
  if (!hasAdminRole(roles)) return <div style={{ padding: 32 }}>Access denied. Admin role required.</div>;

  const handleCreate = async () => {
    setCreateError(null);
    try {
      await api.createSynonymRule({
        rule: newRule,
        groupName: newGroup,
        description: newDescription || undefined,
      });
      setNewRule('');
      setNewGroup('general');
      setNewDescription('');
      setShowCreate(false);
      loadRules(selectedGroup);
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Failed to create rule.');
    }
  };

  const handleUpdate = async (id: string) => {
    try {
      await api.updateSynonymRule(id, {
        rule: editRule,
        groupName: editGroup,
        description: editDescription,
      });
      setEditingId(null);
      loadRules(selectedGroup);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update rule.');
    }
  };

  const handleToggleActive = async (rule: SynonymRuleResponse) => {
    try {
      await api.updateSynonymRule(rule.id, { isActive: !rule.isActive });
      loadRules(selectedGroup);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to toggle rule.');
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await api.deleteSynonymRule(id);
      loadRules(selectedGroup);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete rule.');
    }
  };

  const handleSync = async () => {
    setSyncing(true);
    setSyncMessage(null);
    try {
      const result = await api.syncSynonymMaps();
      setSyncMessage(
        result.success
          ? `Synced ${result.ruleCount} rules to Azure AI Search.`
          : `Sync failed: ${result.errorDetail}`,
      );
    } catch (e) {
      setSyncMessage(e instanceof Error ? e.message : 'Sync failed.');
    } finally {
      setSyncing(false);
    }
  };

  const handleSeed = async () => {
    try {
      const result = await api.seedSynonymRules(false);
      setSyncMessage(`Seeded ${result.seeded} default synonym rules.`);
      loadRules(selectedGroup);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to seed defaults.');
    }
  };

  const startEdit = (rule: SynonymRuleResponse) => {
    setEditingId(rule.id);
    setEditRule(rule.rule);
    setEditGroup(rule.groupName);
    setEditDescription(rule.description ?? '');
  };

  return (
    <div style={{ padding: 32, maxWidth: 960, margin: '0 auto' }}>
      <div style={{ marginBottom: 16, display: 'flex', gap: 12 }}>
        <Link to="/admin">Connectors</Link>
        <Link to="/patterns">Patterns</Link>
        <Link to="/diagnostics">Diagnostics</Link>
        <strong>Synonyms</strong>
        <Link to="/">Chat</Link>
      </div>

      <h1>Synonym Management</h1>
      <p style={{ color: '#666', marginBottom: 16 }}>
        Manage domain vocabulary synonyms to improve search recall. Changes take effect after syncing to Azure AI Search.
      </p>

      {error && <div role="alert" style={{ color: 'red', marginBottom: 12 }}>{error}</div>}
      {syncMessage && <div style={{ color: '#0070f3', marginBottom: 12 }}>{syncMessage}</div>}

      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <button onClick={handleSync} disabled={syncing}>
          {syncing ? 'Syncing...' : 'Sync to Search'}
        </button>
        <button onClick={handleSeed}>Seed Defaults</button>
        <button onClick={() => setShowCreate(!showCreate)}>
          {showCreate ? 'Cancel' : 'Add Rule'}
        </button>
        <select
          value={selectedGroup}
          onChange={(e) => setSelectedGroup(e.target.value)}
          style={{ marginLeft: 'auto' }}
        >
          <option value="">All Groups</option>
          {groups.map((g) => (
            <option key={g} value={g}>{g}</option>
          ))}
        </select>
      </div>

      {showCreate && (
        <div style={{ border: '1px solid #ccc', borderRadius: 4, padding: 12, marginBottom: 16 }}>
          <h3 style={{ marginTop: 0 }}>New Synonym Rule</h3>
          {createError && <div style={{ color: 'red', marginBottom: 8 }}>{createError}</div>}
          <div style={{ marginBottom: 8 }}>
            <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>
              Rule (Solr format)
            </label>
            <input
              type="text"
              value={newRule}
              onChange={(e) => setNewRule(e.target.value)}
              placeholder='crash, BSOD, blue screen  or  BSOD => blue screen of death'
              style={{ width: '100%', padding: 6 }}
            />
            <small style={{ color: '#888' }}>
              Equivalent: &quot;term1, term2, term3&quot; | Explicit: &quot;input =&gt; expansion&quot;
            </small>
          </div>
          <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Group</label>
              <input
                type="text"
                value={newGroup}
                onChange={(e) => setNewGroup(e.target.value)}
                style={{ width: '100%', padding: 6 }}
              />
            </div>
            <div style={{ flex: 2 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Description</label>
              <input
                type="text"
                value={newDescription}
                onChange={(e) => setNewDescription(e.target.value)}
                placeholder="Optional description"
                style={{ width: '100%', padding: 6 }}
              />
            </div>
          </div>
          <button onClick={handleCreate} disabled={!newRule.trim()}>Create</button>
        </div>
      )}

      {loading ? (
        <div>Loading rules...</div>
      ) : rules.length === 0 ? (
        <div style={{ color: '#888', padding: 24, textAlign: 'center' }}>
          No synonym rules found. Click &quot;Seed Defaults&quot; to add common support domain synonyms.
        </div>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '2px solid #ccc', textAlign: 'left' }}>
              <th style={{ padding: 8 }}>Group</th>
              <th style={{ padding: 8 }}>Rule</th>
              <th style={{ padding: 8 }}>Description</th>
              <th style={{ padding: 8 }}>Active</th>
              <th style={{ padding: 8 }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={rule.id} style={{ borderBottom: '1px solid #eee' }}>
                {editingId === rule.id ? (
                  <>
                    <td style={{ padding: 8 }}>
                      <input value={editGroup} onChange={(e) => setEditGroup(e.target.value)} style={{ width: '100%', padding: 4 }} />
                    </td>
                    <td style={{ padding: 8 }}>
                      <input value={editRule} onChange={(e) => setEditRule(e.target.value)} style={{ width: '100%', padding: 4 }} />
                    </td>
                    <td style={{ padding: 8 }}>
                      <input value={editDescription} onChange={(e) => setEditDescription(e.target.value)} style={{ width: '100%', padding: 4 }} />
                    </td>
                    <td style={{ padding: 8 }}>{rule.isActive ? 'Yes' : 'No'}</td>
                    <td style={{ padding: 8 }}>
                      <button onClick={() => handleUpdate(rule.id)} style={{ marginRight: 4 }}>Save</button>
                      <button onClick={() => setEditingId(null)}>Cancel</button>
                    </td>
                  </>
                ) : (
                  <>
                    <td style={{ padding: 8 }}>
                      <span style={{ background: '#eee', borderRadius: 4, padding: '2px 6px', fontSize: 12 }}>
                        {rule.groupName}
                      </span>
                    </td>
                    <td style={{ padding: 8, fontFamily: 'monospace', fontSize: 13 }}>{rule.rule}</td>
                    <td style={{ padding: 8, color: '#666', fontSize: 13 }}>{rule.description ?? ''}</td>
                    <td style={{ padding: 8 }}>
                      <button
                        onClick={() => handleToggleActive(rule)}
                        style={{ background: rule.isActive ? '#4caf50' : '#ccc', color: '#fff', border: 'none', borderRadius: 4, padding: '2px 8px', cursor: 'pointer' }}
                      >
                        {rule.isActive ? 'Yes' : 'No'}
                      </button>
                    </td>
                    <td style={{ padding: 8 }}>
                      <button onClick={() => startEdit(rule)} style={{ marginRight: 4 }}>Edit</button>
                      <button onClick={() => handleDelete(rule.id)} style={{ color: 'red' }}>Delete</button>
                    </td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <div style={{ marginTop: 16, color: '#888', fontSize: 12 }}>
        Total: {rules.length} rules across {groups.length} groups
      </div>
    </div>
  );
}
