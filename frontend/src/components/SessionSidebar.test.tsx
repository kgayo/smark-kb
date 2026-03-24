import { render, screen, fireEvent } from '@testing-library/react';
import { SessionSidebar } from './SessionSidebar';
import type { SessionResponse } from '../api/types';

const mockSession: SessionResponse = {
  sessionId: 'sess-1',
  tenantId: 'tenant-1',
  userId: 'user-1',
  title: 'Deploy issue',
  customerRef: null,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  expiresAt: null,
  messageCount: 5,
};

describe('SessionSidebar', () => {
  it('renders session list', () => {
    render(
      <SessionSidebar
        sessions={[mockSession]}
        activeSessionId={null}
        onSelect={() => {}}
        onNew={() => {}}
        onDelete={() => {}}
      />,
    );
    expect(screen.getByText('Deploy issue')).toBeInTheDocument();
    expect(screen.getByText(/5 msgs/)).toBeInTheDocument();
  });

  it('shows empty state when no sessions', () => {
    render(
      <SessionSidebar
        sessions={[]}
        activeSessionId={null}
        onSelect={() => {}}
        onNew={() => {}}
        onDelete={() => {}}
      />,
    );
    expect(screen.getByText('No sessions yet. Start a new one!')).toBeInTheDocument();
  });

  it('highlights active session', () => {
    const { container } = render(
      <SessionSidebar
        sessions={[mockSession]}
        activeSessionId="sess-1"
        onSelect={() => {}}
        onNew={() => {}}
        onDelete={() => {}}
      />,
    );
    const activeItem = container.querySelector('.session-item.active');
    expect(activeItem).toBeInTheDocument();
  });

  it('calls onSelect when session clicked', () => {
    const onSelect = vi.fn();
    render(
      <SessionSidebar
        sessions={[mockSession]}
        activeSessionId={null}
        onSelect={onSelect}
        onNew={() => {}}
        onDelete={() => {}}
      />,
    );
    fireEvent.click(screen.getByTestId('session-sess-1'));
    expect(onSelect).toHaveBeenCalledWith('sess-1');
  });

  it('calls onNew when new button clicked', () => {
    const onNew = vi.fn();
    render(
      <SessionSidebar
        sessions={[]}
        activeSessionId={null}
        onSelect={() => {}}
        onNew={onNew}
        onDelete={() => {}}
      />,
    );
    fireEvent.click(screen.getByTestId('new-session-btn'));
    expect(onNew).toHaveBeenCalledOnce();
  });

  it('calls onDelete when delete button clicked', () => {
    const onDelete = vi.fn();
    render(
      <SessionSidebar
        sessions={[mockSession]}
        activeSessionId={null}
        onSelect={() => {}}
        onNew={() => {}}
        onDelete={onDelete}
      />,
    );
    fireEvent.click(screen.getByTestId('delete-session-sess-1'));
    expect(onDelete).toHaveBeenCalledWith('sess-1');
  });

  it('shows Untitled session for sessions without title', () => {
    const untitled = { ...mockSession, title: null };
    render(
      <SessionSidebar
        sessions={[untitled]}
        activeSessionId={null}
        onSelect={() => {}}
        onNew={() => {}}
        onDelete={() => {}}
      />,
    );
    expect(screen.getByText('Untitled session')).toBeInTheDocument();
  });

  it('has aria-labels on interactive elements', () => {
    render(
      <SessionSidebar
        sessions={[mockSession]}
        activeSessionId={null}
        onSelect={() => {}}
        onNew={() => {}}
        onDelete={() => {}}
      />,
    );
    expect(screen.getByLabelText('Create new session')).toBeInTheDocument();
    expect(screen.getByLabelText('Open session Deploy issue')).toBeInTheDocument();
    expect(screen.getByLabelText('Delete session Deploy issue')).toBeInTheDocument();
  });
});
