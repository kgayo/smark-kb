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

### Usage Metrics

The detail view includes a **Usage Metrics** section showing how often a pattern is cited in chat answers:

- Total citations, 7/30/90-day citation counts
- Unique users who received this pattern
- Average confidence of answers citing it
- First and last citation timestamps
- 30-day daily breakdown bar chart

### Version History

A **Version History** table tracks field-level changes across governance transitions:

| Column | Description |
|--------|-------------|
| Date | When the transition occurred |
| Change | Type of governance action (Reviewed, Approved, Deprecated) |
| By | Actor who performed the transition |
| Changed Fields | List of fields affected |

## Contradiction Detection

Admins can scan for conflicting patterns within the same product area:

- **Duplicate patterns**: high combined similarity across symptoms, problem statement, title, and resolution steps
- **Resolution conflicts**: similar problem domain but diverging resolutions
- **Symptom overlap**: significant overlap in symptom descriptions

Detected contradictions appear in a reviewable queue. Each can be **resolved** (with notes explaining the resolution) or left pending for further analysis.

## Maintenance Tasks

Automated scans detect three maintenance issue types:

| Type | Detection Criteria | Severity |
|------|-------------------|----------|
| **Stale** | Not updated beyond threshold (default 90 days) | Warning (Critical if >2x threshold) |
| **Low Quality** | Quality score below threshold (default 0.4) | Warning (Critical if <0.2) |
| **Unused** | Not cited in any answer within threshold (default 60 days) | Warning |

Tasks can be **resolved** (with action taken) or **dismissed** (with reason). No automated pattern changes occur without human approval.

## API Endpoints Used

- `GET /api/patterns/governance-queue` — list patterns (filterable by trust level, product area)
- `GET /api/patterns/{id}` — get pattern details
- `GET /api/patterns/{id}/history` — get version history
- `POST /api/patterns/{id}/review` — mark as reviewed
- `POST /api/patterns/{id}/approve` — approve (auto-indexes to search)
- `POST /api/patterns/{id}/deprecate` — deprecate (removes from index)
- `GET /api/admin/patterns/{id}/usage` — get usage metrics
- `GET /api/admin/patterns/candidates` — find distillation candidates
- `POST /api/admin/patterns/distill` — trigger auto-distillation
- `POST /api/admin/patterns/detect-contradictions` — scan for contradictions
- `GET /api/admin/patterns/contradictions` — list detected contradictions
- `POST /api/admin/patterns/contradictions/{id}/resolve` — resolve contradiction
- `POST /api/admin/patterns/detect-maintenance` — scan for maintenance issues
- `GET /api/admin/patterns/maintenance-tasks` — list maintenance tasks
- `POST /api/admin/patterns/maintenance-tasks/{id}/resolve` — resolve task
- `POST /api/admin/patterns/maintenance-tasks/{id}/dismiss` — dismiss task
