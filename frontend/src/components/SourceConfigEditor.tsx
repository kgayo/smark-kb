import { useState, useEffect, useCallback } from 'react';
import { logger } from '../utils/logger';
import type {
  ConnectorType,
  AzureDevOpsSourceConfig,
  SharePointSourceConfig,
  HubSpotSourceConfig,
  ClickUpSourceConfig,
} from '../api/types';
import { ConnectorTypes } from '../constants/enums';

export interface SourceConfigEditorProps {
  connectorType: ConnectorType;
  value: string;
  onChange: (json: string) => void;
  readOnly?: boolean;
}

// ── Default configs ──

function defaultAdoConfig(): AzureDevOpsSourceConfig {
  return {
    organizationUrl: '',
    projects: [],
    ingestWorkItems: true,
    ingestWikiPages: true,
    workItemTypes: [],
    areaPaths: [],
    batchSize: 200,
  };
}

function defaultSharePointConfig(): SharePointSourceConfig {
  return {
    siteUrl: '',
    entraIdTenantId: '',
    clientId: '',
    driveIds: [],
    ingestDocumentLibraries: true,
    includeExtensions: [],
    excludeFolders: [],
    batchSize: 200,
  };
}

function defaultHubSpotConfig(): HubSpotSourceConfig {
  return {
    portalId: '',
    baseUrl: 'https://api.hubapi.com',
    objectTypes: ['tickets'],
    customProperties: [],
    pipelines: [],
    batchSize: 100,
  };
}

function defaultClickUpConfig(): ClickUpSourceConfig {
  return {
    workspaceId: '',
    baseUrl: 'https://api.clickup.com',
    spaceIds: [],
    folderIds: [],
    listIds: [],
    ingestTasks: true,
    ingestDocs: true,
    taskStatuses: [],
    batchSize: 100,
  };
}

// ── Helpers ──

function parseJsonSafe<T extends object>(json: string, fallback: T): T {
  if (!json.trim()) return fallback;
  try {
    const parsed = JSON.parse(json);
    // Merge with defaults so missing fields get fallback values
    return { ...fallback, ...parsed } as T;
  } catch (e) {
    logger.warn('[SourceConfigEditor] Failed to parse source config JSON', e);
    return fallback;
  }
}

function splitCsv(val: string): string[] {
  return val
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
}

function joinCsv(arr: string[]): string {
  return arr.join(', ');
}

interface TagInputProps {
  value: string[];
  onChange: (val: string[]) => void;
  placeholder?: string;
  readOnly?: boolean;
  testId?: string;
  ariaLabel?: string;
}

function TagInput({ value, onChange, placeholder, readOnly, testId, ariaLabel }: TagInputProps) {
  const [text, setText] = useState(joinCsv(value));

  useEffect(() => {
    setText(joinCsv(value));
  }, [value]);

  function handleBlur() {
    onChange(splitCsv(text));
  }

  return (
    <input
      type="text"
      value={text}
      onChange={(e) => setText(e.target.value)}
      onBlur={handleBlur}
      placeholder={placeholder}
      readOnly={readOnly}
      data-testid={testId}
      aria-label={ariaLabel}
    />
  );
}

// ── Per-type forms ──

function AdoForm({
  config,
  onChange,
  readOnly,
}: {
  config: AzureDevOpsSourceConfig;
  onChange: (c: AzureDevOpsSourceConfig) => void;
  readOnly?: boolean;
}) {
  return (
    <div className="source-config-fields" data-testid="ado-config-form">
      <div className="draft-field">
        <label className="draft-field-label">Organization URL *</label>
        <input
          type="text"
          value={config.organizationUrl}
          onChange={(e) => onChange({ ...config, organizationUrl: e.target.value })}
          placeholder="https://dev.azure.com/myorg"
          readOnly={readOnly}
          data-testid="ado-org-url"
          aria-label="Organization URL"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Projects</label>
        <TagInput
          value={config.projects}
          onChange={(v) => onChange({ ...config, projects: v })}
          placeholder="ProjectA, ProjectB"
          readOnly={readOnly}
          testId="ado-projects"
          ariaLabel="Projects"
        />
        <span className="field-hint">Comma-separated. Leave empty to ingest all projects.</span>
      </div>
      <div className="draft-field-row">
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={config.ingestWorkItems}
            onChange={(e) => onChange({ ...config, ingestWorkItems: e.target.checked })}
            disabled={readOnly}
            data-testid="ado-ingest-work-items"
          />
          Ingest work items
        </label>
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={config.ingestWikiPages}
            onChange={(e) => onChange({ ...config, ingestWikiPages: e.target.checked })}
            disabled={readOnly}
            data-testid="ado-ingest-wiki"
          />
          Ingest wiki pages
        </label>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Work Item Types</label>
        <TagInput
          value={config.workItemTypes}
          onChange={(v) => onChange({ ...config, workItemTypes: v })}
          placeholder="Bug, Task, User Story"
          readOnly={readOnly}
          testId="ado-work-item-types"
          ariaLabel="Work Item Types"
        />
        <span className="field-hint">Leave empty for all types.</span>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Area Paths</label>
        <TagInput
          value={config.areaPaths}
          onChange={(v) => onChange({ ...config, areaPaths: v })}
          placeholder="Project\\Area1, Project\\Area2"
          readOnly={readOnly}
          testId="ado-area-paths"
          ariaLabel="Area Paths"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Batch Size</label>
        <input
          type="number"
          value={config.batchSize}
          onChange={(e) => onChange({ ...config, batchSize: parseInt(e.target.value) || 200 })}
          min={1}
          max={1000}
          readOnly={readOnly}
          data-testid="ado-batch-size"
          aria-label="Batch Size"
        />
      </div>
    </div>
  );
}

function SharePointForm({
  config,
  onChange,
  readOnly,
}: {
  config: SharePointSourceConfig;
  onChange: (c: SharePointSourceConfig) => void;
  readOnly?: boolean;
}) {
  return (
    <div className="source-config-fields" data-testid="sharepoint-config-form">
      <div className="draft-field">
        <label className="draft-field-label">Site URL *</label>
        <input
          type="text"
          value={config.siteUrl}
          onChange={(e) => onChange({ ...config, siteUrl: e.target.value })}
          placeholder="https://contoso.sharepoint.com/sites/support"
          readOnly={readOnly}
          data-testid="sp-site-url"
          aria-label="Site URL"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Entra ID Tenant ID *</label>
        <input
          type="text"
          value={config.entraIdTenantId}
          onChange={(e) => onChange({ ...config, entraIdTenantId: e.target.value })}
          placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
          readOnly={readOnly}
          data-testid="sp-tenant-id"
          aria-label="Entra ID Tenant ID"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Client ID *</label>
        <input
          type="text"
          value={config.clientId}
          onChange={(e) => onChange({ ...config, clientId: e.target.value })}
          placeholder="App registration client ID"
          readOnly={readOnly}
          data-testid="sp-client-id"
          aria-label="Client ID"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Drive IDs</label>
        <TagInput
          value={config.driveIds}
          onChange={(v) => onChange({ ...config, driveIds: v })}
          placeholder="drive-id-1, drive-id-2"
          readOnly={readOnly}
          testId="sp-drive-ids"
          ariaLabel="Drive IDs"
        />
        <span className="field-hint">Leave empty to discover all document libraries.</span>
      </div>
      <div className="draft-field-row">
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={config.ingestDocumentLibraries}
            onChange={(e) => onChange({ ...config, ingestDocumentLibraries: e.target.checked })}
            disabled={readOnly}
            data-testid="sp-ingest-doc-libs"
          />
          Ingest document libraries
        </label>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Include Extensions</label>
        <TagInput
          value={config.includeExtensions}
          onChange={(v) => onChange({ ...config, includeExtensions: v })}
          placeholder=".pdf, .docx, .md"
          readOnly={readOnly}
          testId="sp-include-ext"
          ariaLabel="Include Extensions"
        />
        <span className="field-hint">Leave empty for all supported file types.</span>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Exclude Folders</label>
        <TagInput
          value={config.excludeFolders}
          onChange={(v) => onChange({ ...config, excludeFolders: v })}
          placeholder="Archive, Old"
          readOnly={readOnly}
          testId="sp-exclude-folders"
          ariaLabel="Exclude Folders"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Batch Size</label>
        <input
          type="number"
          value={config.batchSize}
          onChange={(e) => onChange({ ...config, batchSize: parseInt(e.target.value) || 200 })}
          min={1}
          max={1000}
          readOnly={readOnly}
          data-testid="sp-batch-size"
          aria-label="Batch Size"
        />
      </div>
    </div>
  );
}

function HubSpotForm({
  config,
  onChange,
  readOnly,
}: {
  config: HubSpotSourceConfig;
  onChange: (c: HubSpotSourceConfig) => void;
  readOnly?: boolean;
}) {
  return (
    <div className="source-config-fields" data-testid="hubspot-config-form">
      <div className="draft-field">
        <label className="draft-field-label">Portal ID *</label>
        <input
          type="text"
          value={config.portalId}
          onChange={(e) => onChange({ ...config, portalId: e.target.value })}
          placeholder="12345678"
          readOnly={readOnly}
          data-testid="hs-portal-id"
          aria-label="Portal ID"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Object Types</label>
        <TagInput
          value={config.objectTypes}
          onChange={(v) => onChange({ ...config, objectTypes: v })}
          placeholder="tickets, contacts, companies"
          readOnly={readOnly}
          testId="hs-object-types"
          ariaLabel="Object Types"
        />
        <span className="field-hint">Default: tickets</span>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Pipelines</label>
        <TagInput
          value={config.pipelines}
          onChange={(v) => onChange({ ...config, pipelines: v })}
          placeholder="Support Pipeline, Sales Pipeline"
          readOnly={readOnly}
          testId="hs-pipelines"
          ariaLabel="Pipelines"
        />
        <span className="field-hint">Leave empty for all pipelines.</span>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Custom Properties</label>
        <TagInput
          value={config.customProperties}
          onChange={(v) => onChange({ ...config, customProperties: v })}
          placeholder="custom_field_1, custom_field_2"
          readOnly={readOnly}
          testId="hs-custom-props"
          ariaLabel="Custom Properties"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Batch Size</label>
        <input
          type="number"
          value={config.batchSize}
          onChange={(e) => onChange({ ...config, batchSize: parseInt(e.target.value) || 100 })}
          min={1}
          max={1000}
          readOnly={readOnly}
          data-testid="hs-batch-size"
          aria-label="Batch Size"
        />
      </div>
    </div>
  );
}

function ClickUpForm({
  config,
  onChange,
  readOnly,
}: {
  config: ClickUpSourceConfig;
  onChange: (c: ClickUpSourceConfig) => void;
  readOnly?: boolean;
}) {
  return (
    <div className="source-config-fields" data-testid="clickup-config-form">
      <div className="draft-field">
        <label className="draft-field-label">Workspace ID *</label>
        <input
          type="text"
          value={config.workspaceId}
          onChange={(e) => onChange({ ...config, workspaceId: e.target.value })}
          placeholder="abc123"
          readOnly={readOnly}
          data-testid="cu-workspace-id"
          aria-label="Workspace ID"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Space IDs</label>
        <TagInput
          value={config.spaceIds}
          onChange={(v) => onChange({ ...config, spaceIds: v })}
          placeholder="space-1, space-2"
          readOnly={readOnly}
          testId="cu-space-ids"
          ariaLabel="Space IDs"
        />
        <span className="field-hint">Leave empty for all spaces.</span>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Folder IDs</label>
        <TagInput
          value={config.folderIds}
          onChange={(v) => onChange({ ...config, folderIds: v })}
          placeholder="folder-1, folder-2"
          readOnly={readOnly}
          testId="cu-folder-ids"
          ariaLabel="Folder IDs"
        />
      </div>
      <div className="draft-field">
        <label className="draft-field-label">List IDs</label>
        <TagInput
          value={config.listIds}
          onChange={(v) => onChange({ ...config, listIds: v })}
          placeholder="list-1, list-2"
          readOnly={readOnly}
          testId="cu-list-ids"
          ariaLabel="List IDs"
        />
      </div>
      <div className="draft-field-row">
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={config.ingestTasks}
            onChange={(e) => onChange({ ...config, ingestTasks: e.target.checked })}
            disabled={readOnly}
            data-testid="cu-ingest-tasks"
          />
          Ingest tasks
        </label>
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={config.ingestDocs}
            onChange={(e) => onChange({ ...config, ingestDocs: e.target.checked })}
            disabled={readOnly}
            data-testid="cu-ingest-docs"
          />
          Ingest docs
        </label>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Task Statuses</label>
        <TagInput
          value={config.taskStatuses}
          onChange={(v) => onChange({ ...config, taskStatuses: v })}
          placeholder="Open, In Progress, Closed"
          readOnly={readOnly}
          testId="cu-task-statuses"
          ariaLabel="Task Statuses"
        />
        <span className="field-hint">Leave empty for all statuses.</span>
      </div>
      <div className="draft-field">
        <label className="draft-field-label">Batch Size</label>
        <input
          type="number"
          value={config.batchSize}
          onChange={(e) => onChange({ ...config, batchSize: parseInt(e.target.value) || 100 })}
          min={1}
          max={1000}
          readOnly={readOnly}
          data-testid="cu-batch-size"
          aria-label="Batch Size"
        />
      </div>
    </div>
  );
}

// ── Main component ──

export function SourceConfigEditor({
  connectorType,
  value,
  onChange,
  readOnly,
}: SourceConfigEditorProps) {
  const [useRawJson, setUseRawJson] = useState(false);
  const [rawJson, setRawJson] = useState(value);

  useEffect(() => {
    setRawJson(value);
  }, [value]);

  const emitChange = useCallback(
    (obj: unknown) => {
      const json = JSON.stringify(obj, null, 2);
      onChange(json);
    },
    [onChange],
  );

  if (useRawJson || readOnly) {
    return (
      <div className="source-config-editor" data-testid="source-config-editor">
        {!readOnly && (
          <div className="source-config-toggle">
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => setUseRawJson(false)}
              data-testid="switch-to-form"
              aria-label="Switch to form editor"
            >
              Switch to form
            </button>
          </div>
        )}
        {readOnly ? (
          <pre className="source-config-pre" data-testid="source-config-display">
            {value || '(none)'}
          </pre>
        ) : (
          <textarea
            value={rawJson}
            onChange={(e) => {
              setRawJson(e.target.value);
              onChange(e.target.value);
            }}
            rows={8}
            data-testid="source-config-raw-json"
            aria-label="Source configuration JSON"
          />
        )}
      </div>
    );
  }

  return (
    <div className="source-config-editor" data-testid="source-config-editor">
      <div className="source-config-toggle">
        <button
          type="button"
          className="btn btn-sm"
          onClick={() => {
            setRawJson(value);
            setUseRawJson(true);
          }}
          data-testid="switch-to-json"
          aria-label="Edit as JSON"
        >
          Edit as JSON
        </button>
      </div>

      {connectorType === ConnectorTypes.AzureDevOps && (
        <AdoForm
          config={parseJsonSafe<AzureDevOpsSourceConfig>(value, defaultAdoConfig())}
          onChange={emitChange}
          readOnly={readOnly}
        />
      )}
      {connectorType === ConnectorTypes.SharePoint && (
        <SharePointForm
          config={parseJsonSafe<SharePointSourceConfig>(value, defaultSharePointConfig())}
          onChange={emitChange}
          readOnly={readOnly}
        />
      )}
      {connectorType === ConnectorTypes.HubSpot && (
        <HubSpotForm
          config={parseJsonSafe<HubSpotSourceConfig>(value, defaultHubSpotConfig())}
          onChange={emitChange}
          readOnly={readOnly}
        />
      )}
      {connectorType === ConnectorTypes.ClickUp && (
        <ClickUpForm
          config={parseJsonSafe<ClickUpSourceConfig>(value, defaultClickUpConfig())}
          onChange={emitChange}
          readOnly={readOnly}
        />
      )}
    </div>
  );
}
