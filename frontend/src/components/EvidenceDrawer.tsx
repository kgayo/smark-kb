import { useState } from 'react';
import type { CitationDto } from '../api/types';
import { SourceViewerPanel } from './SourceViewerPanel';
import { formatDateTime } from '../utils/dateFormat';

interface EvidenceDrawerProps {
  citations: CitationDto[];
  open: boolean;
  onClose: () => void;
}

const formatDate = formatDateTime;

export function EvidenceDrawer({ citations, open, onClose }: EvidenceDrawerProps) {
  const [viewingChunkId, setViewingChunkId] = useState<string | null>(null);

  if (!open) return null;

  if (viewingChunkId) {
    return (
      <aside className="evidence-drawer" data-testid="evidence-drawer" role="complementary">
        <SourceViewerPanel
          chunkId={viewingChunkId}
          onBack={() => setViewingChunkId(null)}
        />
      </aside>
    );
  }

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
            <div className="citation-actions">
              <button
                className="citation-view-btn"
                data-testid="view-source-btn"
                onClick={() => setViewingChunkId(c.chunkId)}
                aria-label={`View content for ${c.title}`}
              >
                View content
              </button>
              {c.sourceUrl && (
                <a
                  href={c.sourceUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="citation-link"
                  aria-label={`Open external source for ${c.title} (opens in new tab)`}
                >
                  Open external
                </a>
              )}
            </div>
          </article>
        ))}
      </div>
    </aside>
  );
}
