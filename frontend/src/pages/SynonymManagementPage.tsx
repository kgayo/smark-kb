import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { logger } from '../utils/logger';
import type {
  SynonymRuleResponse,
  SynonymRuleListResponse,
  StopWordResponse,
  StopWordListResponse,
  SpecialTokenResponse,
  SpecialTokenListResponse,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';

type Tab = 'synonyms' | 'stop-words' | 'special-tokens';

export function SynonymManagementPage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [activeTab, setActiveTab] = useState<Tab>('synonyms');

  if (rolesLoading) return <div style={{ padding: 32 }}>Loading...</div>;
  if (!hasAdminRole(roles)) return <div style={{ padding: 32 }}>Access denied. Admin role required.</div>;

  const tabStyle = (tab: Tab) => ({
    padding: '8px 16px',
    border: 'none',
    borderBottom: activeTab === tab ? '2px solid #0070f3' : '2px solid transparent',
    background: 'none',
    cursor: 'pointer',
    fontWeight: activeTab === tab ? 'bold' as const : 'normal' as const,
    color: activeTab === tab ? '#0070f3' : '#666',
  });

  return (
    <div style={{ padding: 32, maxWidth: 960, margin: '0 auto' }}>
      <div style={{ marginBottom: 16, display: 'flex', gap: 12 }}>
        <Link to="/admin">Connectors</Link>
        <Link to="/patterns">Patterns</Link>
        <Link to="/diagnostics">Diagnostics</Link>
        <strong>Search Vocabulary</strong>
        <Link to="/">Chat</Link>
      </div>

      <h1>Search Vocabulary Management</h1>
      <p style={{ color: '#666', marginBottom: 16 }}>
        Manage synonyms, stop words, and special tokens to improve search quality.
      </p>

      <div style={{ borderBottom: '1px solid #ccc', marginBottom: 16 }}>
        <button style={tabStyle('synonyms')} onClick={() => setActiveTab('synonyms')} aria-label="Synonyms tab">Synonyms</button>
        <button style={tabStyle('stop-words')} onClick={() => setActiveTab('stop-words')} aria-label="Stop Words tab">Stop Words</button>
        <button style={tabStyle('special-tokens')} onClick={() => setActiveTab('special-tokens')} aria-label="Special Tokens tab">Special Tokens</button>
      </div>

      {activeTab === 'synonyms' && <SynonymsTab />}
      {activeTab === 'stop-words' && <StopWordsTab />}
      {activeTab === 'special-tokens' && <SpecialTokensTab />}
    </div>
  );
}

// ── Synonyms Tab (existing P3-004 functionality) ──

function SynonymsTab() {
  const [rules, setRules] = useState<SynonymRuleResponse[]>([]);
  const [groups, setGroups] = useState<string[]>([]);
  const [selectedGroup, setSelectedGroup] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [newRule, setNewRule] = useState('');
  const [newGroup, setNewGroup] = useState('general');
  const [newDescription, setNewDescription] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
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
      logger.warn('Failed to load synonym rules', e);
      setError(e instanceof Error ? e.message : 'Failed to load synonym rules.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadRules(selectedGroup); }, [loadRules, selectedGroup]);

  const handleCreate = async () => {
    setCreateError(null);
    try {
      await api.createSynonymRule({ rule: newRule, groupName: newGroup, description: newDescription || undefined });
      setNewRule(''); setNewGroup('general'); setNewDescription(''); setShowCreate(false);
      loadRules(selectedGroup);
    } catch (e) { logger.warn('Failed to create synonym rule', e); setCreateError(e instanceof Error ? e.message : 'Failed to create rule.'); }
  };

  const handleUpdate = async (id: string) => {
    try {
      await api.updateSynonymRule(id, { rule: editRule, groupName: editGroup, description: editDescription });
      setEditingId(null);
      loadRules(selectedGroup);
    } catch (e) { logger.warn('Failed to update synonym rule', e); setError(e instanceof Error ? e.message : 'Failed to update rule.'); }
  };

  const handleToggleActive = async (rule: SynonymRuleResponse) => {
    try {
      await api.updateSynonymRule(rule.id, { isActive: !rule.isActive });
      loadRules(selectedGroup);
    } catch (e) { logger.warn('Failed to toggle synonym rule', e); setError(e instanceof Error ? e.message : 'Failed to toggle rule.'); }
  };

  const handleDelete = async (id: string) => {
    try { await api.deleteSynonymRule(id); loadRules(selectedGroup); }
    catch (e) { logger.warn('Failed to delete synonym rule', e); setError(e instanceof Error ? e.message : 'Failed to delete rule.'); }
  };

  const handleSync = async () => {
    setSyncing(true); setSyncMessage(null);
    try {
      const result = await api.syncSynonymMaps();
      setSyncMessage(result.success ? `Synced ${result.ruleCount} rules to Azure AI Search.` : `Sync failed: ${result.errorDetail}`);
    } catch (e) { logger.warn('Failed to sync synonym maps', e); setSyncMessage(e instanceof Error ? e.message : 'Sync failed.'); }
    finally { setSyncing(false); }
  };

  const handleSeed = async () => {
    try {
      const result = await api.seedSynonymRules(false);
      setSyncMessage(`Seeded ${result.seeded} default synonym rules.`);
      loadRules(selectedGroup);
    } catch (e) { logger.warn('Failed to seed synonym defaults', e); setError(e instanceof Error ? e.message : 'Failed to seed defaults.'); }
  };

  const startEdit = (rule: SynonymRuleResponse) => {
    setEditingId(rule.id); setEditRule(rule.rule); setEditGroup(rule.groupName); setEditDescription(rule.description ?? '');
  };

  return (
    <div>
      {error && <div role="alert" style={{ color: 'red', marginBottom: 12 }}>{error}</div>}
      {syncMessage && <div style={{ color: '#0070f3', marginBottom: 12 }}>{syncMessage}</div>}

      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <button onClick={handleSync} disabled={syncing} aria-label={syncing ? 'Syncing synonym rules' : 'Sync synonym rules to search'}>{syncing ? 'Syncing...' : 'Sync to Search'}</button>
        <button onClick={handleSeed} aria-label="Seed default synonym rules">Seed Defaults</button>
        <button onClick={() => setShowCreate(!showCreate)} aria-label={showCreate ? 'Cancel adding synonym rule' : 'Add synonym rule'}>{showCreate ? 'Cancel' : 'Add Rule'}</button>
        <select value={selectedGroup} onChange={(e) => setSelectedGroup(e.target.value)} aria-label="Filter by group" style={{ marginLeft: 'auto' }}>
          <option value="">All Groups</option>
          {groups.map((g) => (<option key={g} value={g}>{g}</option>))}
        </select>
      </div>

      {showCreate && (
        <div style={{ border: '1px solid #ccc', borderRadius: 4, padding: 12, marginBottom: 16 }}>
          <h3 style={{ marginTop: 0 }}>New Synonym Rule</h3>
          {createError && <div style={{ color: 'red', marginBottom: 8 }}>{createError}</div>}
          <div style={{ marginBottom: 8 }}>
            <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Rule (Solr format)</label>
            <input type="text" value={newRule} onChange={(e) => setNewRule(e.target.value)}
              placeholder='crash, BSOD, blue screen  or  BSOD => blue screen of death' style={{ width: '100%', padding: 6 }}
              aria-label="Synonym rule in Solr format" />
            <small style={{ color: '#888' }}>Equivalent: &quot;term1, term2, term3&quot; | Explicit: &quot;input =&gt; expansion&quot;</small>
          </div>
          <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Group</label>
              <input type="text" value={newGroup} onChange={(e) => setNewGroup(e.target.value)} style={{ width: '100%', padding: 6 }}
                aria-label="Synonym group" />
            </div>
            <div style={{ flex: 2 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Description</label>
              <input type="text" value={newDescription} onChange={(e) => setNewDescription(e.target.value)} placeholder="Optional description" style={{ width: '100%', padding: 6 }}
                aria-label="Synonym rule description" />
            </div>
          </div>
          <button onClick={handleCreate} disabled={!newRule.trim()} aria-label="Create synonym rule">Create</button>
        </div>
      )}

      {loading ? <div>Loading rules...</div> : rules.length === 0 ? (
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
                    <td style={{ padding: 8 }}><input value={editGroup} onChange={(e) => setEditGroup(e.target.value)} style={{ width: '100%', padding: 4 }} aria-label="Edit synonym group name" /></td>
                    <td style={{ padding: 8 }}><input value={editRule} onChange={(e) => setEditRule(e.target.value)} style={{ width: '100%', padding: 4 }} aria-label="Edit synonym rule value" /></td>
                    <td style={{ padding: 8 }}><input value={editDescription} onChange={(e) => setEditDescription(e.target.value)} style={{ width: '100%', padding: 4 }} aria-label="Edit synonym rule description" /></td>
                    <td style={{ padding: 8 }}>{rule.isActive ? 'Yes' : 'No'}</td>
                    <td style={{ padding: 8 }}>
                      <button onClick={() => handleUpdate(rule.id)} style={{ marginRight: 4 }} aria-label="Save synonym rule changes">Save</button>
                      <button onClick={() => setEditingId(null)} aria-label="Cancel editing synonym rule">Cancel</button>
                    </td>
                  </>
                ) : (
                  <>
                    <td style={{ padding: 8 }}><span style={{ background: '#eee', borderRadius: 4, padding: '2px 6px', fontSize: 12 }}>{rule.groupName}</span></td>
                    <td style={{ padding: 8, fontFamily: 'monospace', fontSize: 13 }}>{rule.rule}</td>
                    <td style={{ padding: 8, color: '#666', fontSize: 13 }}>{rule.description ?? ''}</td>
                    <td style={{ padding: 8 }}>
                      <button onClick={() => handleToggleActive(rule)} style={{ background: rule.isActive ? '#4caf50' : '#ccc', color: '#fff', border: 'none', borderRadius: 4, padding: '2px 8px', cursor: 'pointer' }} aria-label={rule.isActive ? 'Deactivate synonym rule' : 'Activate synonym rule'}>{rule.isActive ? 'Yes' : 'No'}</button>
                    </td>
                    <td style={{ padding: 8 }}>
                      <button onClick={() => startEdit(rule)} style={{ marginRight: 4 }} aria-label="Edit synonym rule">Edit</button>
                      <button onClick={() => handleDelete(rule.id)} style={{ color: 'red' }} aria-label="Delete synonym rule">Delete</button>
                    </td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <div style={{ marginTop: 16, color: '#888', fontSize: 12 }}>Total: {rules.length} rules across {groups.length} groups</div>
    </div>
  );
}

// ── Stop Words Tab (P3-028) ──

function StopWordsTab() {
  const [words, setWords] = useState<StopWordResponse[]>([]);
  const [groups, setGroups] = useState<string[]>([]);
  const [selectedGroup, setSelectedGroup] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [newWord, setNewWord] = useState('');
  const [newGroup, setNewGroup] = useState('general');
  const [createError, setCreateError] = useState<string | null>(null);

  const loadWords = useCallback(async (group?: string) => {
    setLoading(true); setError(null);
    try {
      const result: StopWordListResponse = await api.listStopWords(group || undefined);
      setWords(result.words);
      setGroups(result.groups);
    } catch (e) { logger.warn('Failed to load stop words', e); setError(e instanceof Error ? e.message : 'Failed to load stop words.'); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { loadWords(selectedGroup); }, [loadWords, selectedGroup]);

  const handleCreate = async () => {
    setCreateError(null);
    try {
      await api.createStopWord({ word: newWord, groupName: newGroup });
      setNewWord(''); setNewGroup('general'); setShowCreate(false);
      loadWords(selectedGroup);
    } catch (e) { logger.warn('Failed to create stop word', e); setCreateError(e instanceof Error ? e.message : 'Failed to create stop word.'); }
  };

  const handleToggleActive = async (word: StopWordResponse) => {
    try {
      await api.updateStopWord(word.id, { isActive: !word.isActive });
      loadWords(selectedGroup);
    } catch (e) { logger.warn('Failed to toggle stop word', e); setError(e instanceof Error ? e.message : 'Failed to toggle stop word.'); }
  };

  const handleDelete = async (id: string) => {
    try { await api.deleteStopWord(id); loadWords(selectedGroup); }
    catch (e) { logger.warn('Failed to delete stop word', e); setError(e instanceof Error ? e.message : 'Failed to delete stop word.'); }
  };

  const handleSeed = async () => {
    try {
      const result = await api.seedStopWords(false);
      setMessage(`Seeded ${result.seeded} default stop words.`);
      loadWords(selectedGroup);
    } catch (e) { logger.warn('Failed to seed stop word defaults', e); setError(e instanceof Error ? e.message : 'Failed to seed defaults.'); }
  };

  return (
    <div>
      <p style={{ color: '#666', marginBottom: 12 }}>
        Stop words are removed from search queries before BM25 matching. They reduce noise from common filler words in support tickets.
      </p>

      {error && <div role="alert" style={{ color: 'red', marginBottom: 12 }}>{error}</div>}
      {message && <div style={{ color: '#0070f3', marginBottom: 12 }}>{message}</div>}

      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <button onClick={handleSeed} aria-label="Seed default stop words">Seed Defaults</button>
        <button onClick={() => setShowCreate(!showCreate)} aria-label={showCreate ? 'Cancel adding stop word' : 'Add stop word'}>{showCreate ? 'Cancel' : 'Add Word'}</button>
        <select value={selectedGroup} onChange={(e) => setSelectedGroup(e.target.value)} aria-label="Filter by group" style={{ marginLeft: 'auto' }}>
          <option value="">All Groups</option>
          {groups.map((g) => (<option key={g} value={g}>{g}</option>))}
        </select>
      </div>

      {showCreate && (
        <div style={{ border: '1px solid #ccc', borderRadius: 4, padding: 12, marginBottom: 16 }}>
          <h3 style={{ marginTop: 0 }}>New Stop Word</h3>
          {createError && <div style={{ color: 'red', marginBottom: 8 }}>{createError}</div>}
          <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
            <div style={{ flex: 2 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Word</label>
              <input type="text" value={newWord} onChange={(e) => setNewWord(e.target.value)}
                placeholder="e.g., hello, please, thanks" style={{ width: '100%', padding: 6 }}
                aria-label="Stop word" />
            </div>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Group</label>
              <input type="text" value={newGroup} onChange={(e) => setNewGroup(e.target.value)} style={{ width: '100%', padding: 6 }}
                aria-label="Synonym group" />
            </div>
          </div>
          <button onClick={handleCreate} disabled={!newWord.trim()} aria-label="Create stop word">Create</button>
        </div>
      )}

      {loading ? <div>Loading stop words...</div> : words.length === 0 ? (
        <div style={{ color: '#888', padding: 24, textAlign: 'center' }}>
          No stop words configured. Click &quot;Seed Defaults&quot; to add common support filler words.
        </div>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '2px solid #ccc', textAlign: 'left' }}>
              <th style={{ padding: 8 }}>Group</th>
              <th style={{ padding: 8 }}>Word</th>
              <th style={{ padding: 8 }}>Active</th>
              <th style={{ padding: 8 }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {words.map((word) => (
              <tr key={word.id} style={{ borderBottom: '1px solid #eee' }}>
                <td style={{ padding: 8 }}>
                  <span style={{ background: '#eee', borderRadius: 4, padding: '2px 6px', fontSize: 12 }}>{word.groupName}</span>
                </td>
                <td style={{ padding: 8, fontFamily: 'monospace', fontSize: 13 }}>{word.word}</td>
                <td style={{ padding: 8 }}>
                  <button onClick={() => handleToggleActive(word)}
                    style={{ background: word.isActive ? '#4caf50' : '#ccc', color: '#fff', border: 'none', borderRadius: 4, padding: '2px 8px', cursor: 'pointer' }}
                    aria-label={word.isActive ? 'Deactivate stop word' : 'Activate stop word'}>
                    {word.isActive ? 'Yes' : 'No'}
                  </button>
                </td>
                <td style={{ padding: 8 }}>
                  <button onClick={() => handleDelete(word.id)} style={{ color: 'red' }} aria-label="Delete stop word">Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <div style={{ marginTop: 16, color: '#888', fontSize: 12 }}>Total: {words.length} stop words across {groups.length} groups</div>
    </div>
  );
}

// ── Special Tokens Tab (P3-028) ──

function SpecialTokensTab() {
  const [tokens, setTokens] = useState<SpecialTokenResponse[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [newToken, setNewToken] = useState('');
  const [newCategory, setNewCategory] = useState('error-code');
  const [newBoost, setNewBoost] = useState(2);
  const [newDescription, setNewDescription] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);

  const loadTokens = useCallback(async (category?: string) => {
    setLoading(true); setError(null);
    try {
      const result: SpecialTokenListResponse = await api.listSpecialTokens(category || undefined);
      setTokens(result.tokens);
      setCategories(result.categories);
    } catch (e) { logger.warn('Failed to load special tokens', e); setError(e instanceof Error ? e.message : 'Failed to load special tokens.'); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { loadTokens(selectedCategory); }, [loadTokens, selectedCategory]);

  const handleCreate = async () => {
    setCreateError(null);
    try {
      await api.createSpecialToken({ token: newToken, category: newCategory, boostFactor: newBoost, description: newDescription || undefined });
      setNewToken(''); setNewCategory('error-code'); setNewBoost(2); setNewDescription(''); setShowCreate(false);
      loadTokens(selectedCategory);
    } catch (e) { logger.warn('Failed to create special token', e); setCreateError(e instanceof Error ? e.message : 'Failed to create special token.'); }
  };

  const handleToggleActive = async (token: SpecialTokenResponse) => {
    try {
      await api.updateSpecialToken(token.id, { isActive: !token.isActive });
      loadTokens(selectedCategory);
    } catch (e) { logger.warn('Failed to toggle special token', e); setError(e instanceof Error ? e.message : 'Failed to toggle special token.'); }
  };

  const handleDelete = async (id: string) => {
    try { await api.deleteSpecialToken(id); loadTokens(selectedCategory); }
    catch (e) { logger.warn('Failed to delete special token', e); setError(e instanceof Error ? e.message : 'Failed to delete special token.'); }
  };

  const handleSeed = async () => {
    try {
      const result = await api.seedSpecialTokens(false);
      setMessage(`Seeded ${result.seeded} default special tokens.`);
      loadTokens(selectedCategory);
    } catch (e) { logger.warn('Failed to seed special token defaults', e); setError(e instanceof Error ? e.message : 'Failed to seed defaults.'); }
  };

  return (
    <div>
      <p style={{ color: '#666', marginBottom: 12 }}>
        Special tokens (error codes, product identifiers) are preserved during query preprocessing and boosted in BM25 ranking for better exact-match recall.
      </p>

      {error && <div role="alert" style={{ color: 'red', marginBottom: 12 }}>{error}</div>}
      {message && <div style={{ color: '#0070f3', marginBottom: 12 }}>{message}</div>}

      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <button onClick={handleSeed} aria-label="Seed default special tokens">Seed Defaults</button>
        <button onClick={() => setShowCreate(!showCreate)} aria-label={showCreate ? 'Cancel adding special token' : 'Add special token'}>{showCreate ? 'Cancel' : 'Add Token'}</button>
        <select value={selectedCategory} aria-label="Filter by category" onChange={(e) => setSelectedCategory(e.target.value)} style={{ marginLeft: 'auto' }}>
          <option value="">All Categories</option>
          {categories.map((c) => (<option key={c} value={c}>{c}</option>))}
        </select>
      </div>

      {showCreate && (
        <div style={{ border: '1px solid #ccc', borderRadius: 4, padding: 12, marginBottom: 16 }}>
          <h3 style={{ marginTop: 0 }}>New Special Token</h3>
          {createError && <div style={{ color: 'red', marginBottom: 8 }}>{createError}</div>}
          <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
            <div style={{ flex: 2 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Token</label>
              <input type="text" value={newToken} onChange={(e) => setNewToken(e.target.value)}
                placeholder='e.g., 0x80070005, HTTP 502, BSOD, AADSTS50076' aria-label="Special token" style={{ width: '100%', padding: 6 }} />
            </div>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Category</label>
              <input type="text" value={newCategory} onChange={(e) => setNewCategory(e.target.value)} aria-label="Token category" style={{ width: '100%', padding: 6 }} />
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Boost Factor (1-10)</label>
              <input type="number" value={newBoost} onChange={(e) => setNewBoost(Number(e.target.value))}
                min={1} max={10} aria-label="Token boost factor" style={{ width: '100%', padding: 6 }} />
              <small style={{ color: '#888' }}>Higher = more weight in BM25 ranking</small>
            </div>
            <div style={{ flex: 2 }}>
              <label style={{ display: 'block', fontWeight: 'bold', marginBottom: 4 }}>Description</label>
              <input type="text" value={newDescription} onChange={(e) => setNewDescription(e.target.value)}
                placeholder="Optional description" aria-label="Special token description" style={{ width: '100%', padding: 6 }} />
            </div>
          </div>
          <button onClick={handleCreate} disabled={!newToken.trim()} aria-label="Create special token">Create</button>
        </div>
      )}

      {loading ? <div>Loading special tokens...</div> : tokens.length === 0 ? (
        <div style={{ color: '#888', padding: 24, textAlign: 'center' }}>
          No special tokens configured. Click &quot;Seed Defaults&quot; to add common error codes and identifiers.
        </div>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '2px solid #ccc', textAlign: 'left' }}>
              <th style={{ padding: 8 }}>Category</th>
              <th style={{ padding: 8 }}>Token</th>
              <th style={{ padding: 8 }}>Boost</th>
              <th style={{ padding: 8 }}>Description</th>
              <th style={{ padding: 8 }}>Active</th>
              <th style={{ padding: 8 }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {tokens.map((token) => (
              <tr key={token.id} style={{ borderBottom: '1px solid #eee' }}>
                <td style={{ padding: 8 }}>
                  <span style={{ background: '#eee', borderRadius: 4, padding: '2px 6px', fontSize: 12 }}>{token.category}</span>
                </td>
                <td style={{ padding: 8, fontFamily: 'monospace', fontSize: 13 }}>{token.token}</td>
                <td style={{ padding: 8, textAlign: 'center' }}>{token.boostFactor}x</td>
                <td style={{ padding: 8, color: '#666', fontSize: 13 }}>{token.description ?? ''}</td>
                <td style={{ padding: 8 }}>
                  <button onClick={() => handleToggleActive(token)}
                    style={{ background: token.isActive ? '#4caf50' : '#ccc', color: '#fff', border: 'none', borderRadius: 4, padding: '2px 8px', cursor: 'pointer' }}
                    aria-label={token.isActive ? 'Deactivate special token' : 'Activate special token'}>
                    {token.isActive ? 'Yes' : 'No'}
                  </button>
                </td>
                <td style={{ padding: 8 }}>
                  <button onClick={() => handleDelete(token.id)} style={{ color: 'red' }} aria-label="Delete special token">Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <div style={{ marginTop: 16, color: '#888', fontSize: 12 }}>Total: {tokens.length} tokens across {categories.length} categories</div>
    </div>
  );
}
