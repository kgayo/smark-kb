# Chat Page

**Route:** `/`
**Required Role:** Any authenticated user (SupportAgent, SupportLead, Admin)
**Component:** `ChatPage.tsx`

The main support assistant interface. Support agents ask questions and receive grounded answers with citations, next steps, and escalation recommendations.

## Layout

```
┌─────────────┬──────────────────────────────────────┬──────────────┐
│             │                                      │              │
│  Session    │         Chat Thread                  │  Evidence    │
│  Sidebar    │                                      │  Drawer      │
│             │  [User message]                      │              │
│  + New      │  [Assistant response]                │  Source 1    │
│  Session 1  │    - Confidence badge                │  Source 2    │
│  Session 2  │    - Citations [1] [2]               │  Source 3    │
│  Session 3  │    - Next steps                      │              │
│             │    - Escalation banner               │              │
│             │    - Feedback widget                 │              │
│             │                                      │              │
│             │  ┌──────────────────────────────┐    │              │
│             │  │ Message input + filters      │    │              │
│             │  └──────────────────────────────┘    │              │
└─────────────┴──────────────────────────────────────┴──────────────┘
```

## Features

### Session Management
- **New session**: click "+" to start a fresh conversation
- **Session list**: sidebar shows all previous sessions with titles
- **Switch sessions**: click any session to load its message history
- **Delete sessions**: remove sessions you no longer need

### Chat Thread
Each assistant response includes:
- **Confidence badge**: High (green) / Medium (yellow) / Low (red) with percentage and rationale explaining why (e.g., chunk count, relevance quality, source diversity, evidence recency)
- **Inline citations**: numbered references `[1]`, `[2]` linking to evidence sources
- **Next steps**: actionable follow-up suggestions based on the answer
- **Typing indicator**: shows when the assistant is generating a response

### Retrieval Filters
Expand the filter panel below the input to narrow search scope:
- **Source type**: filter by connector type (Azure DevOps, SharePoint, etc.)
- **Product area**: filter by product/component
- **Time horizon**: limit to recent evidence (last 30/90/180 days)
- **Tags**: filter by custom tags

### Evidence Drawer
Click any citation to open the evidence drawer on the right, showing:
- Source document title
- Relevant text snippet
- Source system and access level
- Direct link to the original document

### Feedback
After each response, provide feedback:
- **Thumbs up/down**: rate answer quality
- **Reason codes**: WrongAnswer, OutdatedInfo, MissingContext, NotRelevant, etc.
- **Correction field**: provide the correct answer for training

### Outcome Recording
At session end, record the resolution:
- Resolved without escalation
- Escalated to a team
- Rerouted to another channel

### Escalation
When confidence is low or the query warrants it, the assistant shows an escalation banner. Click to open the **Escalation Draft Modal** which pre-fills:
- Summary of the issue
- Steps already attempted
- Recommended target team
- Supporting evidence

## API Endpoints Used

- `POST /api/sessions` — create session
- `GET /api/sessions` — list sessions
- `POST /api/sessions/{id}/messages` — send message
- `GET /api/sessions/{id}/messages` — load history
- `POST /api/sessions/{id}/messages/{mid}/feedback` — submit feedback
- `POST /api/sessions/{id}/outcome` — record outcome
- `POST /api/escalations/draft` — create escalation draft
