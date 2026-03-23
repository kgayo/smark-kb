# Routing Analytics Page

**Route:** `/routing`
**Required Role:** Admin
**Component:** `RoutingAnalyticsPage.tsx`

Analyze escalation routing quality, manage routing rules, and review system-generated improvement recommendations.

## Tabs

### Analytics Tab

**Summary Cards**: Total Outcomes, Self-Resolution Rate, Acceptance Rate, Reroute Rate for the selected time window.

**Time Window Selector**: 7, 14, 30, or 90 days.

**Team Metrics Table**: per-team escalation count, acceptance rate, and reroute rate.

**Product Area Metrics Table**: per-product-area routing performance with team, escalation count, acceptance rate, reroute rate, and average resolution time.

### Rules Tab

Manage per-tenant routing rules that control escalation team targeting.

| Field | Description |
|-------|-------------|
| Product Area | The product area this rule matches |
| Target Team | Team to route escalations to |
| Escalation Threshold | Confidence threshold below which escalation triggers |
| Min Severity | Minimum severity (P1-P4) for the rule to apply |
| Active | Whether the rule is currently in effect |

Actions: create, edit inline, delete. Rule count shown as tab badge.

### Recommendations Tab

System-generated improvement suggestions based on outcome pattern analysis.

| Column | Description |
|--------|-------------|
| Type | TeamChange or ThresholdAdjust |
| Product Area | Affected area |
| Current Team | Currently routed team |
| Suggested Team | Recommended team (for TeamChange) |
| Confidence | Algorithm confidence in the recommendation |
| Source | Eval Report link or "Manual" |

**Generate**: click to trigger analysis of recent outcomes.
**Apply**: accept recommendation and update routing rule.
**Dismiss**: reject recommendation with no rule change.

Filter by status: All, Pending, Applied, Dismissed.

## API Endpoints Used

- `GET /api/admin/routing/analytics` — routing metrics summary
- `GET /api/admin/routing-rules` — list routing rules
- `POST /api/admin/routing-rules` — create rule
- `PUT /api/admin/routing-rules/{id}` — update rule
- `DELETE /api/admin/routing-rules/{id}` — delete rule
- `POST /api/admin/routing/recommendations/generate` — generate recommendations
- `GET /api/admin/routing/recommendations` — list recommendations
- `POST /api/admin/routing/recommendations/{id}/apply` — apply recommendation
- `POST /api/admin/routing/recommendations/{id}/dismiss` — dismiss recommendation
