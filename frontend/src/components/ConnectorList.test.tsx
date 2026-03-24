import { render, screen, fireEvent } from '@testing-library/react';
import { ConnectorList } from './ConnectorList';
import type { ConnectorResponse } from '../api/types';

const mockConnector: ConnectorResponse = {
  id: 'conn-1',
  name: 'Production ADO',
  connectorType: 'AzureDevOps',
  status: 'Enabled',
  authType: 'Pat',
  hasSecret: true,
  sourceConfig: '{"org":"myorg"}',
  fieldMapping: null,
  scheduleCron: null,
  createdAt: '2026-03-10T00:00:00Z',
  updatedAt: '2026-03-15T12:00:00Z',
  lastSyncRun: {
    id: 'run-1',
    status: 'Completed',
    isBackfill: false,
    startedAt: '2026-03-15T11:00:00Z',
    completedAt: '2026-03-15T11:05:00Z',
    recordsProcessed: 150,
    recordsFailed: 0,
    errorDetail: null,
  },
};

const disabledConnector: ConnectorResponse = {
  ...mockConnector,
  id: 'conn-2',
  name: 'Staging SP',
  connectorType: 'SharePoint',
  status: 'Disabled',
  lastSyncRun: null,
};

describe('ConnectorList', () => {
  it('renders connector table with data', () => {
    render(
      <ConnectorList connectors={[mockConnector]} onSelect={() => {}} onCreate={() => {}} />,
    );
    expect(screen.getByTestId('connector-table')).toBeInTheDocument();
    expect(screen.getByText('Production ADO')).toBeInTheDocument();
    expect(screen.getByText('AzureDevOps')).toBeInTheDocument();
    expect(screen.getByText('Enabled')).toBeInTheDocument();
  });

  it('shows empty state when no connectors', () => {
    render(<ConnectorList connectors={[]} onSelect={() => {}} onCreate={() => {}} />);
    expect(screen.getByTestId('connector-empty')).toBeInTheDocument();
    expect(screen.getByText(/No connectors configured/)).toBeInTheDocument();
  });

  it('calls onSelect when row clicked', () => {
    const onSelect = vi.fn();
    render(
      <ConnectorList connectors={[mockConnector]} onSelect={onSelect} onCreate={() => {}} />,
    );
    fireEvent.click(screen.getByTestId('connector-row-conn-1'));
    expect(onSelect).toHaveBeenCalledWith('conn-1');
  });

  it('calls onCreate when button clicked', () => {
    const onCreate = vi.fn();
    render(
      <ConnectorList connectors={[]} onSelect={() => {}} onCreate={onCreate} />,
    );
    fireEvent.click(screen.getByTestId('create-connector-btn'));
    expect(onCreate).toHaveBeenCalledOnce();
  });

  it('shows last sync status with record count', () => {
    render(
      <ConnectorList connectors={[mockConnector]} onSelect={() => {}} onCreate={() => {}} />,
    );
    expect(screen.getByText(/Completed/)).toBeInTheDocument();
    expect(screen.getByText(/150 records/)).toBeInTheDocument();
  });

  it('shows Never for connectors with no sync runs', () => {
    render(
      <ConnectorList connectors={[disabledConnector]} onSelect={() => {}} onCreate={() => {}} />,
    );
    expect(screen.getByText('Never')).toBeInTheDocument();
  });

  it('renders multiple connectors', () => {
    render(
      <ConnectorList
        connectors={[mockConnector, disabledConnector]}
        onSelect={() => {}}
        onCreate={() => {}}
      />,
    );
    expect(screen.getByText('Production ADO')).toBeInTheDocument();
    expect(screen.getByText('Staging SP')).toBeInTheDocument();
  });

  it('has aria-label on create connector button', () => {
    render(<ConnectorList connectors={[]} onSelect={() => {}} onCreate={() => {}} />);
    expect(screen.getByLabelText('Create new connector')).toBeInTheDocument();
  });
});
