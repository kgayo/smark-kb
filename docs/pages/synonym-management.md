# Search Vocabulary Management Page

**Route:** `/synonyms`
**Required Role:** Admin
**Component:** `SynonymManagementPage.tsx`

Manage synonyms, stop words, and special tokens to improve search quality. The page has three tabs.

## Synonyms Tab

Synonym rules tell Azure AI Search to treat related terms as equivalent, so queries like "BSOD" also find results mentioning "blue screen."

### Synonym Format

Rules use Solr synonym format:

| Type | Syntax | Meaning |
|------|--------|---------|
| **Equivalent** | `crash, BSOD, blue screen` | All terms treated as interchangeable |
| **Explicit** | `BSOD => blue screen of death` | Left side expands to right side only |

### Operations

- **Create New Rule**: inline form with rule text, group, and description
- **Edit Rule**: click edit to modify group, rule text, or description inline
- **Toggle Active**: enable/disable a rule without deleting it
- **Delete Rule**: permanently remove a rule
- **Sync to Search**: push all active rules to Azure AI Search (applies to both Evidence and Pattern indexes)
- **Seed Defaults**: load 20 default seed synonym rules across 3 groups
- **Group Filter**: dropdown to filter rules by group

### API Endpoints

- `GET /api/admin/synonym-rules` — list all rules
- `POST /api/admin/synonym-rules` — create rule
- `PUT /api/admin/synonym-rules/{id}` — update rule
- `DELETE /api/admin/synonym-rules/{id}` — delete rule
- `POST /api/admin/synonym-rules/sync` — sync to Azure AI Search
- `POST /api/admin/synonym-rules/seed` — load default seeds

## Stop Words Tab

Stop words are removed from search queries before BM25 matching. They reduce noise from common filler words in support tickets (e.g., "hello", "please", "thanks").

### Operations

- **Add Word**: single word, case-insensitive (normalized to lowercase)
- **Toggle Active**: enable/disable without deleting
- **Delete**: permanently remove
- **Seed Defaults**: load 15 default stop words across 2 groups (greeting, filler)
- **Group Filter**: dropdown to filter by group

### How Stop Words Affect Search

1. When a user sends a chat query, the retrieval service calls `PreprocessQueryAsync`
2. Active stop words for the tenant are loaded from SQL
3. Stop words are removed from the BM25 search query text
4. Special tokens (see below) are preserved even if they match stop words
5. The vector embedding search is unaffected (uses original query)

### API Endpoints

- `GET /api/admin/stop-words` — list stop words (optional `?groupName=` filter)
- `GET /api/admin/stop-words/{id}` — get single stop word
- `POST /api/admin/stop-words` — create stop word
- `PUT /api/admin/stop-words/{id}` — update stop word
- `DELETE /api/admin/stop-words/{id}` — delete stop word
- `POST /api/admin/stop-words/seed` — load default seeds

## Special Tokens Tab

Special tokens (error codes, product identifiers) are preserved during query preprocessing and boosted in BM25 ranking for better exact-match recall.

### Token Properties

| Field | Description |
|-------|-------------|
| Token | The exact string to match (e.g., "0x80070005", "HTTP 502", "BSOD") |
| Category | Grouping (error-code, hex-error, http-status, aad-error) |
| Boost Factor | 1-10; token is repeated this many times in BM25 query for ranking weight |
| Description | Optional human-readable explanation |

### Operations

- **Add Token**: token string, category, boost factor (1-10), optional description
- **Toggle Active**: enable/disable without deleting
- **Delete**: permanently remove
- **Seed Defaults**: load 14 default special tokens across 4 categories
- **Category Filter**: dropdown to filter by category

### How Special Tokens Affect Search

1. During query preprocessing, special tokens are detected in the query text
2. Matched tokens are preserved (never removed by stop-word filtering)
3. Tokens are repeated in the BM25 query based on their boost factor (e.g., boost=3 means the token appears 3 times total)
4. This increases the BM25 score for documents containing the exact error code

### Default Seed Categories

- **error-code**: BSOD (boost 3)
- **hex-error**: 0x80070005, 0x80004005, 0x800700E1 (boost 3)
- **http-status**: HTTP 400-503 (boost 2)
- **aad-error**: AADSTS50076, AADSTS700016, AADSTS90002 (boost 3)

### API Endpoints

- `GET /api/admin/special-tokens` — list tokens (optional `?category=` filter)
- `GET /api/admin/special-tokens/{id}` — get single token
- `POST /api/admin/special-tokens` — create token
- `PUT /api/admin/special-tokens/{id}` — update token
- `DELETE /api/admin/special-tokens/{id}` — delete token
- `POST /api/admin/special-tokens/seed` — load default seeds
