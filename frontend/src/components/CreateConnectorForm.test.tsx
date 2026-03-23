import { render, screen, fireEvent } from '@testing-library/react';
import { CreateConnectorForm } from './CreateConnectorForm';

describe('CreateConnectorForm', () => {
  const defaultProps = {
    onSubmit: vi.fn().mockResolvedValue(undefined),
    onCancel: vi.fn(),
    submitting: false,
    error: null,
  };

  it('renders wizard step 1 (type) initially', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    expect(screen.getByTestId('wizard-step-type')).toBeInTheDocument();
    expect(screen.getByTestId('connector-name-input')).toBeInTheDocument();
  });

  it('disables Next when name is empty', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    expect(screen.getByTestId('wizard-next-btn')).toBeDisabled();
  });

  it('enables Next when name is filled', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    fireEvent.change(screen.getByTestId('connector-name-input'), {
      target: { value: 'My Connector' },
    });
    expect(screen.getByTestId('wizard-next-btn')).not.toBeDisabled();
  });

  it('advances to auth step', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    fireEvent.change(screen.getByTestId('connector-name-input'), {
      target: { value: 'My Connector' },
    });
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    expect(screen.getByTestId('wizard-step-auth')).toBeInTheDocument();
    expect(screen.getByTestId('auth-type-select')).toBeInTheDocument();
  });

  it('advances through all steps to review', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    fireEvent.change(screen.getByTestId('connector-name-input'), {
      target: { value: 'Test Conn' },
    });
    // type -> auth
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    // auth -> config
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    expect(screen.getByTestId('wizard-step-config')).toBeInTheDocument();
    // config -> review
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    expect(screen.getByTestId('wizard-step-review')).toBeInTheDocument();
    expect(screen.getByText('Test Conn')).toBeInTheDocument();
    expect(screen.getByTestId('wizard-create-btn')).toBeInTheDocument();
  });

  it('calls onSubmit with correct data on create', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<CreateConnectorForm {...defaultProps} onSubmit={onSubmit} />);
    fireEvent.change(screen.getByTestId('connector-name-input'), {
      target: { value: 'My ADO' },
    });
    fireEvent.click(screen.getByTestId('wizard-next-btn')); // -> auth
    fireEvent.click(screen.getByTestId('wizard-next-btn')); // -> config
    fireEvent.click(screen.getByTestId('wizard-next-btn')); // -> review
    fireEvent.click(screen.getByTestId('wizard-create-btn'));
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'My ADO',
        connectorType: 'AzureDevOps',
        authType: 'Pat',
      }),
    );
  });

  it('calls onCancel when cancel clicked', () => {
    const onCancel = vi.fn();
    render(<CreateConnectorForm {...defaultProps} onCancel={onCancel} />);
    fireEvent.click(screen.getByText('Cancel'));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it('shows error when provided', () => {
    render(<CreateConnectorForm {...defaultProps} error="Something went wrong" />);
    expect(screen.getByTestId('create-error')).toHaveTextContent('Something went wrong');
  });

  it('shows connector type cards', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    expect(screen.getByText('Azure DevOps')).toBeInTheDocument();
    expect(screen.getByText('SharePoint')).toBeInTheDocument();
    expect(screen.getByText('HubSpot')).toBeInTheDocument();
    expect(screen.getByText('ClickUp')).toBeInTheDocument();
  });

  it('wizard buttons have aria-labels', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    expect(screen.getByRole('button', { name: 'Cancel connector creation' })).toBeInTheDocument();
    expect(screen.getByTestId('wizard-next-btn')).toHaveAttribute('aria-label', 'Go to next step');
    // Fill name and advance to auth step to check Back button
    fireEvent.change(screen.getByTestId('connector-name-input'), { target: { value: 'Test' } });
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    expect(screen.getByRole('button', { name: 'Go to previous step' })).toBeInTheDocument();
  });

  it('form inputs have aria-labels', () => {
    render(<CreateConnectorForm {...defaultProps} />);
    expect(screen.getByTestId('connector-name-input')).toHaveAttribute('aria-label', 'Connector name');
    // Navigate to auth step
    fireEvent.change(screen.getByTestId('connector-name-input'), { target: { value: 'Test' } });
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    expect(screen.getByTestId('auth-type-select')).toHaveAttribute('aria-label', 'Authentication type');
    expect(screen.getByTestId('secret-name-input')).toHaveAttribute('aria-label', 'Key Vault secret name');
    // Navigate to config step
    fireEvent.click(screen.getByTestId('wizard-next-btn'));
    expect(screen.getByTestId('schedule-input')).toHaveAttribute('aria-label', 'Sync schedule cron expression');
  });
});
