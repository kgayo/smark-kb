import type { SessionResponse } from '../api/types';

interface SessionSidebarProps {
  sessions: SessionResponse[];
  activeSessionId: string | null;
  onSelect: (sessionId: string) => void;
  onNew: () => void;
  onDelete: (sessionId: string) => void;
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export function SessionSidebar({
  sessions,
  activeSessionId,
  onSelect,
  onNew,
  onDelete,
}: SessionSidebarProps) {
  return (
    <nav className="session-sidebar" data-testid="session-sidebar" aria-label="Chat sessions">
      <div className="sidebar-header">
        <h2>Sessions</h2>
        <button
          onClick={onNew}
          className="btn btn-primary btn-sm"
          data-testid="new-session-btn"
        >
          + New
        </button>
      </div>
      <ul className="session-list">
        {sessions.map((s) => (
          <li
            key={s.sessionId}
            className={`session-item ${s.sessionId === activeSessionId ? 'active' : ''}`}
          >
            <button
              className="session-select-btn"
              onClick={() => onSelect(s.sessionId)}
              data-testid={`session-${s.sessionId}`}
            >
              <span className="session-title">
                {s.title || 'Untitled session'}
              </span>
              <span className="session-meta">
                {s.messageCount} msg{s.messageCount !== 1 ? 's' : ''} &middot;{' '}
                {timeAgo(s.updatedAt)}
              </span>
            </button>
            <button
              className="btn-delete"
              onClick={(e) => {
                e.stopPropagation();
                onDelete(s.sessionId);
              }}
              aria-label={`Delete session ${s.title || 'Untitled'}`}
              data-testid={`delete-session-${s.sessionId}`}
            >
              &times;
            </button>
          </li>
        ))}
        {sessions.length === 0 && (
          <li className="session-empty">No sessions yet. Start a new one!</li>
        )}
      </ul>
    </nav>
  );
}
