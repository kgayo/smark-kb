# Gold Dataset Page

**Route:** `/gold-cases`
**Required Role:** Admin
**Component:** `GoldDatasetPage.tsx`

Manage the gold evaluation dataset — curated test cases used by the eval CLI to measure answer quality. Three tabs organize dataset workflows.

## Tabs

### Cases Tab

Paginated table of gold cases with tag filtering and detail inspection.

**Filter:**
- **Tag** — text filter to narrow cases by tag (e.g., `auth`, `billing`)

**Table columns:**
| Column | Description |
|--------|-------------|
| Case ID | Unique eval identifier (e.g., `eval-00100`) |
| Query | The test query text |
| Response Type | Expected response type (`final_answer`, `clarification`, `escalation`) |
| Tags | Comma-separated tag list |
| Created | Creation timestamp |

**Case detail:** Click any row to show expanded detail including:
- Case ID, query, response type, tags, created/updated timestamps
- Expected criteria: `mustInclude` keywords, `mustCiteSources`, `shouldHaveEvidence`
- Delete button (with confirmation dialog)

### Create Tab

Form to add a new gold case to the dataset.

**Fields:**
- **Case ID** — must match format `eval-NNNNN` (at least 10 characters)
- **Response Type** — select: `final_answer`, `clarification`, `escalation`
- **Query** — free text (minimum 5 characters)
- **Must Include** — comma-separated keywords the answer must contain
- **Tags** — comma-separated tags for filtering
- **Must Cite Sources** — checkbox
- **Should Have Evidence** — checkbox

**Submission:** Creates the case via API, shows success message, and clears the form.

### Export Tab

Download the full gold dataset as NDJSON (newline-delimited JSON) for use with the eval CLI.

**Download:** Click "Download JSONL" to fetch the dataset and trigger a browser file download. The file is named `gold-cases.jsonl`.

## API Endpoints Used

| Endpoint | Method | Permission | Purpose |
|----------|--------|------------|---------|
| `/api/admin/eval/gold-cases` | GET | `connector:manage` | Paginated case list with tag filter |
| `/api/admin/eval/gold-cases/{id}` | GET | `connector:manage` | Case detail |
| `/api/admin/eval/gold-cases` | POST | `connector:manage` | Create case |
| `/api/admin/eval/gold-cases/{id}` | PUT | `connector:manage` | Update case |
| `/api/admin/eval/gold-cases/{id}` | DELETE | `connector:manage` | Delete case |
| `/api/admin/eval/gold-cases/export` | GET | `connector:manage` | NDJSON export |
| `/api/admin/eval/gold-cases/promote` | POST | `connector:manage` | Promote from feedback |

## Navigation

Accessible from the Admin (Connectors) page via the "Gold Dataset" link in the header nav.
