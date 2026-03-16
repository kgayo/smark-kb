import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FeedbackWidget } from './FeedbackWidget';

describe('FeedbackWidget', () => {
  it('renders thumbs up and thumbs down buttons', () => {
    render(<FeedbackWidget messageId="msg-1" onSubmit={vi.fn()} />);
    expect(screen.getByTestId('thumbs-up')).toBeInTheDocument();
    expect(screen.getByTestId('thumbs-down')).toBeInTheDocument();
  });

  it('submits thumbs up immediately', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<FeedbackWidget messageId="msg-1" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('thumbs-up'));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith('msg-1', {
        type: 'ThumbsUp',
        reasonCodes: [],
      });
    });

    expect(screen.getByTestId('feedback-thanks')).toBeInTheDocument();
  });

  it('shows detail form on thumbs down', () => {
    render(<FeedbackWidget messageId="msg-1" onSubmit={vi.fn()} />);

    fireEvent.click(screen.getByTestId('thumbs-down'));

    expect(screen.getByTestId('feedback-details')).toBeInTheDocument();
    expect(screen.getByTestId('reason-codes')).toBeInTheDocument();
    expect(screen.getByTestId('feedback-comment')).toBeInTheDocument();
    expect(screen.getByTestId('feedback-correction')).toBeInTheDocument();
    expect(screen.getByTestId('submit-feedback')).toBeInTheDocument();
  });

  it('renders all reason code checkboxes', () => {
    render(<FeedbackWidget messageId="msg-1" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('thumbs-down'));

    expect(screen.getByTestId('reason-WrongAnswer')).toBeInTheDocument();
    expect(screen.getByTestId('reason-OutdatedInfo')).toBeInTheDocument();
    expect(screen.getByTestId('reason-MissingContext')).toBeInTheDocument();
    expect(screen.getByTestId('reason-WrongSource')).toBeInTheDocument();
    expect(screen.getByTestId('reason-TooVague')).toBeInTheDocument();
    expect(screen.getByTestId('reason-WrongEscalation')).toBeInTheDocument();
    expect(screen.getByTestId('reason-Other')).toBeInTheDocument();
  });

  it('submits thumbs down with selected reasons and comment', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<FeedbackWidget messageId="msg-2" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('thumbs-down'));
    fireEvent.click(screen.getByTestId('reason-WrongAnswer'));
    fireEvent.click(screen.getByTestId('reason-OutdatedInfo'));
    fireEvent.change(screen.getByTestId('feedback-comment'), {
      target: { value: 'This is outdated.' },
    });
    fireEvent.click(screen.getByTestId('submit-feedback'));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith('msg-2', {
        type: 'ThumbsDown',
        reasonCodes: ['WrongAnswer', 'OutdatedInfo'],
        comment: 'This is outdated.',
        correctedAnswer: undefined,
      });
    });

    expect(screen.getByTestId('feedback-thanks')).toBeInTheDocument();
  });

  it('submits corrected answer when provided', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<FeedbackWidget messageId="msg-3" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('thumbs-down'));
    fireEvent.click(screen.getByTestId('reason-WrongAnswer'));
    fireEvent.change(screen.getByTestId('feedback-correction'), {
      target: { value: 'The correct answer is X.' },
    });
    fireEvent.click(screen.getByTestId('submit-feedback'));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith('msg-3', expect.objectContaining({
        correctedAnswer: 'The correct answer is X.',
      }));
    });
  });

  it('shows existing feedback state', () => {
    render(
      <FeedbackWidget
        messageId="msg-4"
        existingFeedback={{ type: 'ThumbsDown', reasonCodes: ['WrongSource'] }}
        onSubmit={vi.fn()}
      />,
    );
    expect(screen.getByTestId('feedback-thanks')).toBeInTheDocument();
    expect(screen.getByTestId('thumbs-down')).toHaveClass('active');
  });

  it('toggles reason codes on and off', () => {
    render(<FeedbackWidget messageId="msg-5" onSubmit={vi.fn()} />);
    fireEvent.click(screen.getByTestId('thumbs-down'));

    const checkbox = screen.getByTestId('reason-MissingContext') as HTMLInputElement;
    expect(checkbox.checked).toBe(false);

    fireEvent.click(checkbox);
    expect(checkbox.checked).toBe(true);

    fireEvent.click(checkbox);
    expect(checkbox.checked).toBe(false);
  });

  it('does not show detail form for thumbs up', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<FeedbackWidget messageId="msg-6" onSubmit={onSubmit} />);

    fireEvent.click(screen.getByTestId('thumbs-up'));

    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    expect(screen.queryByTestId('feedback-details')).not.toBeInTheDocument();
  });
});
