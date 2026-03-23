import { useCallback, useRef, useState } from 'react';
import type { RetrievalFilter } from '../api/types';

export interface RetrievalFilterPanelProps {
  filters: RetrievalFilter;
  onChange: (filters: RetrievalFilter) => void;
}

const SOURCE_TYPE_OPTIONS = ['Ticket', 'Document', 'WikiPage', 'Task', 'CasePattern'];
const TIME_HORIZON_OPTIONS = [
  { label: 'Any time', value: 0 },
  { label: 'Last 7 days', value: 7 },
  { label: 'Last 30 days', value: 30 },
  { label: 'Last 90 days', value: 90 },
  { label: 'Last 180 days', value: 180 },
  { label: 'Last 365 days', value: 365 },
];

export function RetrievalFilterPanel({ filters, onChange }: RetrievalFilterPanelProps) {
  const [expanded, setExpanded] = useState(false);

  const hasActiveFilters =
    (filters.sourceTypes && filters.sourceTypes.length > 0) ||
    (filters.productAreas && filters.productAreas.length > 0) ||
    (filters.timeHorizonDays && filters.timeHorizonDays > 0) ||
    (filters.tags && filters.tags.length > 0);

  const toggleSourceType = useCallback(
    (type: string) => {
      const current = filters.sourceTypes ?? [];
      const next = current.includes(type)
        ? current.filter((t) => t !== type)
        : [...current, type];
      onChange({ ...filters, sourceTypes: next.length > 0 ? next : undefined });
    },
    [filters, onChange],
  );

  const handleTimeHorizonChange = useCallback(
    (e: React.ChangeEvent<HTMLSelectElement>) => {
      const value = parseInt(e.target.value, 10);
      onChange({ ...filters, timeHorizonDays: value > 0 ? value : undefined });
    },
    [filters, onChange],
  );

  const handleProductAreaChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const value = e.target.value.trim();
      const areas = value
        ? value.split(',').map((s) => s.trim()).filter(Boolean)
        : undefined;
      onChange({ ...filters, productAreas: areas && areas.length > 0 ? areas : undefined });
    },
    [filters, onChange],
  );

  const handleTagsChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const value = e.target.value.trim();
      const tags = value
        ? value.split(',').map((s) => s.trim()).filter(Boolean)
        : undefined;
      onChange({ ...filters, tags: tags && tags.length > 0 ? tags : undefined });
    },
    [filters, onChange],
  );

  const productAreasRef = useRef<HTMLInputElement>(null);
  const tagsRef = useRef<HTMLInputElement>(null);

  const handleClearAll = useCallback(() => {
    if (productAreasRef.current) productAreasRef.current.value = '';
    if (tagsRef.current) tagsRef.current.value = '';
    onChange({});
  }, [onChange]);

  return (
    <div className="retrieval-filter-panel" data-testid="filter-panel">
      <button
        className="filter-toggle-btn"
        onClick={() => setExpanded(!expanded)}
        data-testid="filter-toggle"
        aria-label={expanded ? 'Collapse retrieval filters' : 'Expand retrieval filters'}
        aria-expanded={expanded}
      >
        Filters{hasActiveFilters ? ' *' : ''}
        <span className={`filter-chevron ${expanded ? 'expanded' : ''}`}>&#9662;</span>
      </button>

      {expanded && (
        <div className="filter-body" data-testid="filter-body">
          <div className="filter-section">
            <label className="filter-label">Source Types</label>
            <div className="filter-chips">
              {SOURCE_TYPE_OPTIONS.map((type) => (
                <button
                  key={type}
                  className={`filter-chip ${(filters.sourceTypes ?? []).includes(type) ? 'active' : ''}`}
                  onClick={() => toggleSourceType(type)}
                  data-testid={`filter-source-${type}`}
                  aria-label={`${(filters.sourceTypes ?? []).includes(type) ? 'Remove' : 'Add'} ${type} source type filter`}
                  aria-pressed={(filters.sourceTypes ?? []).includes(type)}
                >
                  {type}
                </button>
              ))}
            </div>
          </div>

          <div className="filter-section">
            <label className="filter-label" htmlFor="time-horizon">Time Horizon</label>
            <select
              id="time-horizon"
              className="filter-select"
              value={filters.timeHorizonDays ?? 0}
              onChange={handleTimeHorizonChange}
              data-testid="filter-time-horizon"
            >
              {TIME_HORIZON_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>

          <div className="filter-section">
            <label className="filter-label" htmlFor="product-areas">Product Areas</label>
            <input
              ref={productAreasRef}
              id="product-areas"
              className="filter-input"
              type="text"
              placeholder="e.g., Auth, Billing"
              defaultValue={(filters.productAreas ?? []).join(', ')}
              onBlur={handleProductAreaChange}
              data-testid="filter-product-areas"
            />
          </div>

          <div className="filter-section">
            <label className="filter-label" htmlFor="tags">Tags</label>
            <input
              ref={tagsRef}
              id="tags"
              className="filter-input"
              type="text"
              placeholder="e.g., SSO, timeout"
              defaultValue={(filters.tags ?? []).join(', ')}
              onBlur={handleTagsChange}
              data-testid="filter-tags"
            />
          </div>

          {hasActiveFilters && (
            <button
              className="filter-clear-btn"
              onClick={handleClearAll}
              data-testid="filter-clear"
              aria-label="Clear all filters"
            >
              Clear all filters
            </button>
          )}
        </div>
      )}
    </div>
  );
}
