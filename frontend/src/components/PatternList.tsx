import type { PatternSummary, TrustLevel } from '../api/types';
import { TrustLevels } from '../constants/enums';
import { trustLevelBadgeClass } from '../utils/cssClassHelpers';
import { formatDateOnly } from '../utils/dateFormat';

interface PatternListProps {
  patterns: PatternSummary[];
  onSelect: (patternId: string) => void;
  selectedPatternId?: string;
  trustLevelFilter: TrustLevel | '';
  onTrustLevelFilterChange: (level: TrustLevel | '') => void;
  totalCount: number;
  page: number;
  hasMore: boolean;
  onPageChange: (page: number) => void;
}

const formatDate = formatDateOnly;

export function PatternList({
  patterns,
  onSelect,
  selectedPatternId,
  trustLevelFilter,
  onTrustLevelFilterChange,
  totalCount,
  page,
  hasMore,
  onPageChange,
}: PatternListProps) {
  return (
    <div className="pattern-list" data-testid="pattern-list">
      <div className="pattern-list-header">
        <h2>Pattern Governance Queue</h2>
        <span className="pattern-count" data-testid="pattern-count">{totalCount} patterns</span>
      </div>

      <div className="pattern-filters">
        <label htmlFor="trust-filter">Trust Level:</label>
        <select
          id="trust-filter"
          value={trustLevelFilter}
          onChange={(e) => onTrustLevelFilterChange(e.target.value as TrustLevel | '')}
          data-testid="trust-filter"
        >
          <option value="">All</option>
          <option value={TrustLevels.Draft}>Draft</option>
          <option value={TrustLevels.Reviewed}>Reviewed</option>
          <option value={TrustLevels.Approved}>Approved</option>
          <option value={TrustLevels.Deprecated}>Deprecated</option>
        </select>
      </div>

      {patterns.length === 0 ? (
        <div className="pattern-empty" data-testid="pattern-empty">
          <p>No patterns found{trustLevelFilter ? ` with trust level "${trustLevelFilter}"` : ''}.</p>
        </div>
      ) : (
        <table className="pattern-table" data-testid="pattern-table" aria-label="Pattern governance queue">
          <thead>
            <tr>
              <th>Title</th>
              <th>Trust Level</th>
              <th>Confidence</th>
              <th>Product Area</th>
              <th>Evidence</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {patterns.map((p) => (
              <tr
                key={p.patternId}
                className={`pattern-row${selectedPatternId === p.patternId ? ' selected' : ''}`}
                onClick={() => onSelect(p.patternId)}
                data-testid={`pattern-row-${p.patternId}`}
                aria-label={`Open pattern ${p.title}`}
              >
                <td className="pattern-title-cell">
                  <span className="pattern-title">{p.title}</span>
                  <span className="pattern-problem">{p.problemStatement}</span>
                </td>
                <td>
                  <span className={trustLevelBadgeClass(p.trustLevel)}>{p.trustLevel}</span>
                </td>
                <td>{(p.confidence * 100).toFixed(0)}%</td>
                <td>{p.productArea ?? '—'}</td>
                <td>{p.relatedEvidenceCount}</td>
                <td>{formatDate(p.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {totalCount > 0 && (
        <div className="pattern-pagination" data-testid="pattern-pagination">
          <button
            className="btn btn-sm"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            aria-label="Previous page"
          >
            Previous
          </button>
          <span>Page {page}</span>
          <button
            className="btn btn-sm"
            disabled={!hasMore}
            onClick={() => onPageChange(page + 1)}
            aria-label="Next page"
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}
