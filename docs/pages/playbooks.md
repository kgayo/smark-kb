# Team Playbooks Page

**Route:** `/playbooks`
**Required Role:** Admin
**Component:** `PlaybooksPage.tsx`

Define standardized handoff procedures per team. Playbooks enforce required fields, severity gates, and checklists when agents create escalation drafts.

## Views

### List View

Table of all team playbooks with columns:

| Column | Description |
|--------|-------------|
| Team Name | Target team name |
| Description | Brief description of the playbook |
| Required Fields | Count of mandatory handoff fields |
| Checklist Items | Count of SOP checklist steps |
| Requires Approval | Whether escalations need approval |
| Active | Whether the playbook is currently enforced |

Click a row to open the detail view. "New Playbook" button to create.

### Detail View

Full playbook content with:
- Description, contact channel, fallback team
- Approval requirement, min severity gate, max concurrent escalations
- Required fields list (from: title, customerSummary, stepsToReproduce, logsIdsRequested, suspectedComponent, severity, targetTeam, reason)
- Checklist items (ordered SOP steps)

Actions: Edit, Delete (with confirmation).

### Create/Edit Form

| Field | Description |
|-------|-------------|
| Team Name | Unique per tenant (create only) |
| Description | Free-text description |
| Contact Channel | Preferred contact method |
| Min Severity | Any, P1, P2, P3, or P4 gate |
| Max Concurrent | Maximum active escalations for this team |
| Fallback Team | Team to route to when this team is at capacity |
| Requires Approval | Toggle for approval gate |
| Required Fields | Chip selector for handoff fields |
| Checklist | Add/remove ordered checklist items |

## API Endpoints Used

- `GET /api/admin/playbooks` — list all playbooks
- `GET /api/admin/playbooks/{id}` — get playbook detail
- `POST /api/admin/playbooks` — create playbook
- `PUT /api/admin/playbooks/{id}` — update playbook
- `DELETE /api/admin/playbooks/{id}` — delete playbook
