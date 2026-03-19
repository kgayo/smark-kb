# Pattern Governance Page

**Route:** `/patterns`
**Required Role:** Admin or SupportLead
**Component:** `PatternGovernancePage.tsx`

Review and govern case patterns through a 4-state trust lifecycle. Patterns are auto-distilled from resolved support cases and require human approval before they influence chat answers.

## Trust Model

```
Draft  →  Reviewed  →  Approved  →  Deprecated
  │                        │              │
  └── (can deprecate) ─────┘              │
                                    (removed from
                                     search index)
```

| State | Meaning | Actions Available |
|-------|---------|-------------------|
| **Draft** | Auto-distilled, not yet reviewed | Mark Reviewed, Approve, Deprecate |
| **Reviewed** | Reviewed by a human, pending approval | Approve, Deprecate |
| **Approved** | Active — influences chat answers, indexed in search | Deprecate |
| **Deprecated** | Retired — removed from search index | None |

## Layout

### List View

Filter patterns by trust level using the filter bar, then browse the paginated table:

| Column | Description |
|--------|-------------|
| Title | Pattern title / short description |
| Problem | Problem statement summary |
| Trust Level | Badge with color: Draft (gray), Reviewed (blue), Approved (green), Deprecated (red) |
| Confidence | Quality confidence percentage |
| Product Area | Related product/component |
| Evidence | Number of linked source evidence items |
| Created | Creation timestamp |

### Detail View

Click a pattern to see the full detail:

- **Problem statement**: what the customer issue is
- **Root cause**: identified root cause (extracted from evidence during distillation, shown when available)
- **Symptoms**: observable indicators that this pattern applies
- **Diagnosis steps**: steps to diagnose the issue
- **Resolution steps**: how to resolve
- **Related evidence**: citations to source tickets/documents
- **Governance actions**: buttons depend on current trust state (Review, Approve, Deprecate)
- **Deprecation**: requires a reason and optional reference to a superseding pattern
- **Notes**: free-text field for governance decision notes

## How Patterns Flow

1. Resolved cases accumulate in the Evidence Store
2. Admin triggers distillation (`POST /api/admin/patterns/distill`)
3. New patterns appear in **Draft** state
4. Support leads review patterns and promote through the trust lifecycle
5. **Approved** patterns are auto-indexed in Azure AI Search and used during chat retrieval
6. **Deprecated** patterns are automatically removed from the search index

## API Endpoints Used

- `GET /api/patterns/governance-queue` — list patterns (filterable by trust level, product area)
- `GET /api/patterns/{id}` — get pattern details
- `POST /api/patterns/{id}/review` — mark as reviewed
- `POST /api/patterns/{id}/approve` — approve (auto-indexes to search)
- `POST /api/patterns/{id}/deprecate` — deprecate (removes from index)
- `GET /api/admin/patterns/candidates` — find distillation candidates
- `POST /api/admin/patterns/distill` — trigger auto-distillation
