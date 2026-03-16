import type { CitationDto } from '../api/types';

interface EvidenceDrawerProps {
  citations: CitationDto[];
  open: boolean;
  onClose: () => void;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function EvidenceDrawer({ citations, open, onClose }: EvidenceDrawerProps) {
  if (!open) return null;

  return (
    <aside className="evidence-drawer" data-testid="evidence-drawer" role="complementary">
      <div className="evidence-drawer-header">
        <h2>Evidence ({citations.length})</h2>
        <button
          onClick={onClose}
          className="btn-close"
          aria-label="Close evidence drawer"
          data-testid="evidence-drawer-close"
        >
          &times;
        </button>
      </div>
      <div className="evidence-drawer-body">
        {citations.length === 0 && <p className="no-evidence">No citations available.</p>}
        {citations.map((c) => (
          <article key={c.chunkId} className="citation-card" data-testid="citation-card">
            <h3 className="citation-title">{c.title}</h3>
            <p className="citation-snippet">{c.snippet}</p>
            <div className="citation-meta">
              <span className="citation-source">{c.sourceSystem}</span>
              <span className="citation-date">{formatDate(c.updatedAt)}</span>
              <span className={`citation-access access-${c.accessLabel.toLowerCase()}`}>
                {c.accessLabel}
              </span>
            </div>
            {c.sourceUrl && (
              <a
                href={c.sourceUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="citation-link"
              >
                View source
              </a>
            )}
          </article>
        ))}
      </div>
    </aside>
  );
}
