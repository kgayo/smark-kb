import { render, screen, fireEvent } from '@testing-library/react';
import { SourceConfigEditor } from './SourceConfigEditor';
import { ConnectorTypes } from '../constants/enums';

describe('SourceConfigEditor', () => {
  const defaultProps = {
    connectorType: ConnectorTypes.AzureDevOps,
    value: '',
    onChange: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Renders correct form per connector type ──

  it('renders ADO form for AzureDevOps type', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.AzureDevOps} />);
    expect(screen.getByTestId('ado-config-form')).toBeInTheDocument();
    expect(screen.getByTestId('ado-org-url')).toBeInTheDocument();
  });

  it('renders SharePoint form for SharePoint type', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.SharePoint} />);
    expect(screen.getByTestId('sharepoint-config-form')).toBeInTheDocument();
    expect(screen.getByTestId('sp-site-url')).toBeInTheDocument();
  });

  it('renders HubSpot form for HubSpot type', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.HubSpot} />);
    expect(screen.getByTestId('hubspot-config-form')).toBeInTheDocument();
    expect(screen.getByTestId('hs-portal-id')).toBeInTheDocument();
  });

  it('renders ClickUp form for ClickUp type', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.ClickUp} />);
    expect(screen.getByTestId('clickup-config-form')).toBeInTheDocument();
    expect(screen.getByTestId('cu-workspace-id')).toBeInTheDocument();
  });

  // ── Populates from existing JSON ──

  it('populates ADO form fields from existing JSON', () => {
    const value = JSON.stringify({
      organizationUrl: 'https://dev.azure.com/contoso',
      projects: ['ProjectA', 'ProjectB'],
      ingestWorkItems: true,
      ingestWikiPages: false,
      workItemTypes: [],
      areaPaths: [],
      batchSize: 200,
    });
    render(<SourceConfigEditor {...defaultProps} value={value} />);
    expect(screen.getByTestId('ado-org-url')).toHaveValue('https://dev.azure.com/contoso');
    expect(screen.getByTestId('ado-projects')).toHaveValue('ProjectA, ProjectB');
    expect(screen.getByTestId('ado-ingest-work-items')).toBeChecked();
    expect(screen.getByTestId('ado-ingest-wiki')).not.toBeChecked();
  });

  it('populates SharePoint form fields from existing JSON', () => {
    const value = JSON.stringify({
      siteUrl: 'https://contoso.sharepoint.com/sites/kb',
      entraIdTenantId: 'tenant-123',
      clientId: 'client-456',
      driveIds: ['d1'],
      ingestDocumentLibraries: true,
      includeExtensions: ['.pdf', '.md'],
      excludeFolders: ['Archive'],
      batchSize: 100,
    });
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.SharePoint} value={value} />);
    expect(screen.getByTestId('sp-site-url')).toHaveValue('https://contoso.sharepoint.com/sites/kb');
    expect(screen.getByTestId('sp-tenant-id')).toHaveValue('tenant-123');
    expect(screen.getByTestId('sp-client-id')).toHaveValue('client-456');
    expect(screen.getByTestId('sp-include-ext')).toHaveValue('.pdf, .md');
  });

  // ── Emits JSON on field change ──

  it('emits JSON when ADO organization URL changes', () => {
    const onChange = vi.fn();
    render(<SourceConfigEditor {...defaultProps} onChange={onChange} />);
    fireEvent.change(screen.getByTestId('ado-org-url'), {
      target: { value: 'https://dev.azure.com/neworg' },
    });
    expect(onChange).toHaveBeenCalled();
    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted.organizationUrl).toBe('https://dev.azure.com/neworg');
  });

  it('emits JSON when checkbox toggled', () => {
    const onChange = vi.fn();
    render(<SourceConfigEditor {...defaultProps} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('ado-ingest-wiki'));
    expect(onChange).toHaveBeenCalled();
    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted.ingestWikiPages).toBe(false);
  });

  it('emits JSON when HubSpot portal ID changes', () => {
    const onChange = vi.fn();
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.HubSpot} onChange={onChange} />);
    fireEvent.change(screen.getByTestId('hs-portal-id'), {
      target: { value: '99999' },
    });
    expect(onChange).toHaveBeenCalled();
    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted.portalId).toBe('99999');
  });

  it('emits JSON when ClickUp workspace ID changes', () => {
    const onChange = vi.fn();
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.ClickUp} onChange={onChange} />);
    fireEvent.change(screen.getByTestId('cu-workspace-id'), {
      target: { value: 'ws-abc' },
    });
    expect(onChange).toHaveBeenCalled();
    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted.workspaceId).toBe('ws-abc');
  });

  // ── Tag input (comma-separated) ──

  it('parses comma-separated tag input on blur', () => {
    const onChange = vi.fn();
    render(<SourceConfigEditor {...defaultProps} onChange={onChange} />);
    const projectsInput = screen.getByTestId('ado-projects');
    fireEvent.change(projectsInput, { target: { value: 'ProjA, ProjB, ProjC' } });
    fireEvent.blur(projectsInput);
    expect(onChange).toHaveBeenCalled();
    const lastCall = onChange.mock.calls[onChange.mock.calls.length - 1][0];
    const emitted = JSON.parse(lastCall);
    expect(emitted.projects).toEqual(['ProjA', 'ProjB', 'ProjC']);
  });

  // ── JSON toggle ──

  it('switches to raw JSON mode', () => {
    render(<SourceConfigEditor {...defaultProps} />);
    fireEvent.click(screen.getByTestId('switch-to-json'));
    expect(screen.getByTestId('source-config-raw-json')).toBeInTheDocument();
    expect(screen.queryByTestId('ado-config-form')).not.toBeInTheDocument();
  });

  it('switches back to form mode from JSON', () => {
    render(<SourceConfigEditor {...defaultProps} />);
    fireEvent.click(screen.getByTestId('switch-to-json'));
    expect(screen.getByTestId('source-config-raw-json')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('switch-to-form'));
    expect(screen.getByTestId('ado-config-form')).toBeInTheDocument();
  });

  // ── Read-only mode ──

  it('renders read-only pre display', () => {
    const value = '{"organizationUrl": "https://dev.azure.com/test"}';
    render(<SourceConfigEditor {...defaultProps} value={value} readOnly />);
    expect(screen.getByTestId('source-config-display')).toHaveTextContent(value);
    expect(screen.queryByTestId('switch-to-json')).not.toBeInTheDocument();
  });

  it('shows (none) when read-only with empty value', () => {
    render(<SourceConfigEditor {...defaultProps} value="" readOnly />);
    expect(screen.getByTestId('source-config-display')).toHaveTextContent('(none)');
  });

  // ── Graceful fallback for invalid JSON ──

  it('uses defaults when value is invalid JSON', () => {
    render(<SourceConfigEditor {...defaultProps} value="not-json" />);
    expect(screen.getByTestId('ado-org-url')).toHaveValue('');
    expect(screen.getByTestId('ado-batch-size')).toHaveValue(200);
  });

  // ── Default values ──

  it('HubSpot defaults include tickets in objectTypes', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.HubSpot} />);
    expect(screen.getByTestId('hs-object-types')).toHaveValue('tickets');
  });

  it('ClickUp defaults have both ingest checkboxes checked', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.ClickUp} />);
    expect(screen.getByTestId('cu-ingest-tasks')).toBeChecked();
    expect(screen.getByTestId('cu-ingest-docs')).toBeChecked();
  });

  it('ADO defaults have both ingest checkboxes checked', () => {
    render(<SourceConfigEditor {...defaultProps} />);
    expect(screen.getByTestId('ado-ingest-work-items')).toBeChecked();
    expect(screen.getByTestId('ado-ingest-wiki')).toBeChecked();
  });

  // ── Accessibility: aria-labels ──

  it('ADO form inputs have aria-labels', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.AzureDevOps} />);
    expect(screen.getByLabelText('Organization URL')).toBeInTheDocument();
    expect(screen.getByLabelText('Projects')).toBeInTheDocument();
    expect(screen.getByLabelText('Work Item Types')).toBeInTheDocument();
    expect(screen.getByLabelText('Area Paths')).toBeInTheDocument();
    expect(screen.getByLabelText('Batch Size')).toBeInTheDocument();
  });

  it('SharePoint form inputs have aria-labels', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.SharePoint} />);
    expect(screen.getByLabelText('Site URL')).toBeInTheDocument();
    expect(screen.getByLabelText('Entra ID Tenant ID')).toBeInTheDocument();
    expect(screen.getByLabelText('Client ID')).toBeInTheDocument();
    expect(screen.getByLabelText('Drive IDs')).toBeInTheDocument();
    expect(screen.getByLabelText('Include Extensions')).toBeInTheDocument();
    expect(screen.getByLabelText('Exclude Folders')).toBeInTheDocument();
    expect(screen.getByLabelText('Batch Size')).toBeInTheDocument();
  });

  it('HubSpot form inputs have aria-labels', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.HubSpot} />);
    expect(screen.getByLabelText('Portal ID')).toBeInTheDocument();
    expect(screen.getByLabelText('Object Types')).toBeInTheDocument();
    expect(screen.getByLabelText('Pipelines')).toBeInTheDocument();
    expect(screen.getByLabelText('Custom Properties')).toBeInTheDocument();
    expect(screen.getByLabelText('Batch Size')).toBeInTheDocument();
  });

  it('ClickUp form inputs have aria-labels', () => {
    render(<SourceConfigEditor {...defaultProps} connectorType={ConnectorTypes.ClickUp} />);
    expect(screen.getByLabelText('Workspace ID')).toBeInTheDocument();
    expect(screen.getByLabelText('Space IDs')).toBeInTheDocument();
    expect(screen.getByLabelText('Folder IDs')).toBeInTheDocument();
    expect(screen.getByLabelText('List IDs')).toBeInTheDocument();
    expect(screen.getByLabelText('Task Statuses')).toBeInTheDocument();
    expect(screen.getByLabelText('Batch Size')).toBeInTheDocument();
  });

  it('raw JSON textarea has aria-label', () => {
    render(<SourceConfigEditor {...defaultProps} />);
    fireEvent.click(screen.getByTestId('switch-to-json'));
    expect(screen.getByLabelText('Source configuration JSON')).toBeInTheDocument();
  });
});
