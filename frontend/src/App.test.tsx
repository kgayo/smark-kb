import { render, screen } from '@testing-library/react';
import { App } from './App';

describe('App', () => {
  it('renders the chat page header by default', () => {
    render(<App />);
    expect(screen.getByText('Smart KB')).toBeInTheDocument();
  });

  it('shows session creation prompt when no session is active', () => {
    render(<App />);
    expect(
      screen.getByText('Select or create a session to start chatting.'),
    ).toBeInTheDocument();
  });

  it('renders the new session button', () => {
    render(<App />);
    expect(screen.getByText('Start new session')).toBeInTheDocument();
  });
});
