import { useCallback, useEffect, useRef, useState } from 'react';
import type { EvidenceContentResponse } from '../api/types';
import * as api from '../api/client';

interface SourceViewerPanelProps {
  chunkId: string;
  onBack: () => void;
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

export function SourceViewerPanel({ chunkId, onBack }: SourceViewerPanelProps) {
  const [content, setContent] = useState<EvidenceContentResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const copyTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (copyTimerRef.current) clearTimeout(copyTimerRef.current);
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    api
      .getEvidenceContent(chunkId)
      .then((data) => {
        if (!cancelled) setContent(data);
      })
      .catch((err) => {
        if (!cancelled)
          setError(err instanceof Error ? err.message : 'Failed to load evidence content');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [chunkId]);

  const handleCopyCitation = useCallback(() => {
    if (!content) return;
    const link = content.sourceUrl || `evidence://${content.chunkId}`;
    navigator.clipboard
      .writeText(link)
      .then(() => {
        setCopied(true);
        if (copyTimerRef.current) clearTimeout(copyTimerRef.current);
        copyTimerRef.current = setTimeout(() => setCopied(false), 2000);
      })
      .catch(() => {
        // Clipboard API may fail in insecure contexts or when denied permission.
      });
  }, [content]);

  if (loading) {
    return (
      <div className="source-viewer" data-testid="source-viewer">
        <div className="source-viewer-header">
          <button onClick={onBack} className="btn-back" data-testid="source-viewer-back" aria-label="Back to citations">
            &larr; Back to citations
          </button>
        </div>
        <div className="source-viewer-body">
          <p className="source-viewer-loading" data-testid="source-viewer-loading">
            Loading evidence content...
          </p>
        </div>
      </div>
    );
  }

  if (error || !content) {
    return (
      <div className="source-viewer" data-testid="source-viewer">
        <div className="source-viewer-header">
          <button onClick={onBack} className="btn-back" data-testid="source-viewer-back" aria-label="Back to citations">
            &larr; Back to citations
          </button>
        </div>
        <div className="source-viewer-body">
          <p className="source-viewer-error" data-testid="source-viewer-error">
            {error || 'Evidence not found.'}
          </p>
        </div>
      </div>
    );
  }

  const displayText = content.rawContent || content.chunkText;
  const isTicket = content.sourceType === 'Ticket' || content.sourceType === 'WorkItem';
  const isWiki = content.sourceType === 'WikiPage';

  return (
    <div className="source-viewer" data-testid="source-viewer">
      <div className="source-viewer-header">
        <button onClick={onBack} className="btn-back" data-testid="source-viewer-back" aria-label="Back to citations">
          &larr; Back to citations
        </button>
        <div className="source-viewer-actions">
          <button
            onClick={handleCopyCitation}
            className="btn btn-sm"
            data-testid="copy-citation-link"
          >
            {copied ? 'Copied!' : 'Copy citation link'}
          </button>
          {content.sourceUrl && (
            <a
              href={content.sourceUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-sm btn-outline"
              data-testid="open-external"
              aria-label="Open external source (opens in new tab)"
            >
              Open external
            </a>
          )}
        </div>
      </div>

      <div className="source-viewer-body">
        <h3 className="source-viewer-title" data-testid="source-viewer-title">
          {content.title}
        </h3>

        <div className="source-viewer-meta">
          <span className="meta-badge" data-testid="source-type-badge">
            {content.sourceType}
          </span>
          <span className="meta-badge">{content.sourceSystem}</span>
          <span className={`meta-badge access-${content.accessLabel.toLowerCase()}`}>
            {content.accessLabel}
          </span>
          <span className="meta-date">{formatDate(content.updatedAt)}</span>
          {content.productArea && (
            <span className="meta-badge meta-product-area">{content.productArea}</span>
          )}
        </div>

        {content.tags.length > 0 && (
          <div className="source-viewer-tags" data-testid="source-viewer-tags">
            {content.tags.map((tag) => (
              <span key={tag} className="tag-chip">
                {tag}
              </span>
            ))}
          </div>
        )}

        {content.chunkContext && (
          <div className="source-viewer-context" data-testid="source-viewer-context">
            <span className="context-label">Section:</span> {content.chunkContext}
          </div>
        )}

        <div
          className={`source-viewer-content ${isWiki ? 'content-wiki' : ''} ${isTicket ? 'content-ticket' : ''}`}
          data-testid="source-viewer-content"
        >
          {isWiki ? (
            <pre className="wiki-content">{displayText}</pre>
          ) : isTicket ? (
            <pre className="ticket-content">{displayText}</pre>
          ) : (
            <pre className="document-content">{displayText}</pre>
          )}
        </div>
      </div>
    </div>
  );
}
