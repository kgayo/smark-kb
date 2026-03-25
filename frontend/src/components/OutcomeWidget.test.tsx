import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { OutcomeWidget } from './OutcomeWidget';

describe('OutcomeWidget', () => {
  it('renders resolution type options', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    expect(screen.getByTestId('outcome-options')).toBeInTheDocument();
    expect(screen.getByTestId('resolution-ResolvedWithoutEscalation')).toBeInTheDocument();
    expect(screen.getByTestId('resolution-Escalated')).toBeInTheDocument();
    expect(screen.getByTestId('resolution-Rerouted')).toBeInTheDocument();
  });

  it('shows submit button disabled when no selection', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    expect(screen.getByTestId('submit-outcome')).toBeDisabled();
  });

  it('enables submit button after selecting resolution type', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('resolution-ResolvedWithoutEscalation'));
    expect(screen.getByTestId('submit-outcome')).not.toBeDisabled();
  });

  it('submits outcome with resolution type', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<OutcomeWidget sessionId="s-1" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('resolution-ResolvedWithoutEscalation'));
    fireEvent.click(screen.getByTestId('submit-outcome'));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith('s-1', {
        resolutionType: 'ResolvedWithoutEscalation',
        targetTeam: undefined,
        acceptance: undefined,
      });
    });

    expect(screen.getByTestId('outcome-thanks')).toBeInTheDocument();
  });

  it('shows target team input for Escalated', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('resolution-Escalated'));
    expect(screen.getByTestId('outcome-target-team')).toBeInTheDocument();
  });

  it('shows target team input for Rerouted', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('resolution-Rerouted'));
    expect(screen.getByTestId('outcome-target-team')).toBeInTheDocument();
  });

  it('does not show target team for ResolvedWithoutEscalation', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('resolution-ResolvedWithoutEscalation'));
    expect(screen.queryByTestId('outcome-target-team')).not.toBeInTheDocument();
  });

  it('shows acceptance radio buttons after selecting resolution type', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('resolution-Escalated'));
    expect(screen.getByTestId('outcome-acceptance')).toBeInTheDocument();
    expect(screen.getByTestId('acceptance-yes')).toBeInTheDocument();
    expect(screen.getByTestId('acceptance-no')).toBeInTheDocument();
  });

  it('submits escalated outcome with target team and acceptance', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<OutcomeWidget sessionId="s-2" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('resolution-Escalated'));
    fireEvent.change(screen.getByTestId('outcome-target-team'), {
      target: { value: 'Engineering' },
    });
    fireEvent.click(screen.getByTestId('acceptance-yes'));
    fireEvent.click(screen.getByTestId('submit-outcome'));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith('s-2', {
        resolutionType: 'Escalated',
        targetTeam: 'Engineering',
        acceptance: true,
      });
    });
  });

  it('shows recorded state for existing outcome', () => {
    render(
      <OutcomeWidget
        sessionId="s-3"
        existingOutcome={{ resolutionType: 'ResolvedWithoutEscalation' }}
        onSubmit={vi.fn()}
      />,
    );
    expect(screen.getByTestId('outcome-thanks')).toBeInTheDocument();
    expect(screen.queryByTestId('outcome-options')).not.toBeInTheDocument();
  });

  it('renders label text', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    expect(screen.getByText('How was this session resolved?')).toBeInTheDocument();
  });

  it('logs warning and stays interactive on submit failure', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const onSubmit = vi.fn().mockRejectedValue(new Error('Network error'));
    render(<OutcomeWidget sessionId="s-1" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('resolution-ResolvedWithoutEscalation'));
    fireEvent.click(screen.getByTestId('submit-outcome'));

    await waitFor(() => {
      expect(warnSpy).toHaveBeenCalledWith(
        '[OutcomeWidget] Failed to record outcome:',
        expect.any(Error),
      );
    });
    // Should not show "Outcome recorded" on failure
    expect(screen.queryByTestId('outcome-thanks')).not.toBeInTheDocument();
    // Should show error banner with role="alert"
    const errorBanner = screen.getByTestId('outcome-error');
    expect(errorBanner).toHaveAttribute('role', 'alert');
    expect(errorBanner).toHaveTextContent('Failed to record outcome');
    // Button should be re-enabled after failure
    expect(screen.getByTestId('submit-outcome')).not.toBeDisabled();
    warnSpy.mockRestore();
  });

  it('has aria-labels on submit button and target team input', () => {
    render(<OutcomeWidget sessionId="s-1" onSubmit={vi.fn()} />);
    expect(screen.getByTestId('submit-outcome')).toHaveAttribute('aria-label', 'Record session outcome');
    fireEvent.click(screen.getByTestId('resolution-Escalated'));
    expect(screen.getByTestId('outcome-target-team')).toHaveAttribute('aria-label', 'Target team for escalation or reroute');
  });
});
