// API types matching backend DTOs (SmartKb.Contracts.Models)

export interface ApiResponse<T> {
  data: T | null;
  error: string | null;
  correlationId: string;
  isSuccess: boolean;
}

export interface SessionResponse {
  sessionId: string;
  tenantId: string;
  userId: string;
  title: string | null;
  customerRef: string | null;
  createdAt: string;
  updatedAt: string;
  expiresAt: string | null;
  messageCount: number;
}

export interface SessionListResponse {
  sessions: SessionResponse[];
  totalCount: number;
}

export interface CreateSessionRequest {
  title?: string;
  customerRef?: string;
}

export interface SendMessageRequest {
  query: string;
  userGroups?: string[];
  maxCitations?: number;
}

export interface MessageResponse {
  messageId: string;
  sessionId: string;
  role: 'user' | 'assistant';
  content: string;
  citations: CitationDto[] | null;
  confidence: number | null;
  confidenceLabel: string | null;
  responseType: string | null;
  traceId: string | null;
  correlationId: string | null;
  createdAt: string;
}

export interface MessageListResponse {
  sessionId: string;
  messages: MessageResponse[];
  totalCount: number;
}

export interface SessionChatResponse {
  session: SessionResponse;
  userMessage: MessageResponse;
  assistantMessage: MessageResponse;
  chatResponse: ChatResponse;
}

export interface ChatResponse {
  responseType: 'final_answer' | 'next_steps_only' | 'escalate';
  answer: string;
  citations: CitationDto[];
  confidence: number;
  confidenceLabel: 'High' | 'Medium' | 'Low';
  nextSteps: string[];
  escalation: EscalationSignal | null;
  traceId: string;
  hasEvidence: boolean;
  systemPromptVersion: string;
  piiRedactedCount: number;
}

export interface CitationDto {
  chunkId: string;
  evidenceId: string;
  title: string;
  sourceUrl: string;
  sourceSystem: string;
  snippet: string;
  updatedAt: string;
  accessLabel: string;
}

export interface EscalationSignal {
  recommended: boolean;
  targetTeam: string;
  reason: string;
  handoffNote: string;
}

export type ConfidenceLevel = 'High' | 'Medium' | 'Low';

// ── Escalation draft types ──

export interface CreateEscalationDraftRequest {
  sessionId: string;
  messageId: string;
  title: string;
  customerSummary: string;
  stepsToReproduce: string;
  logsIdsRequested: string;
  suspectedComponent: string;
  severity: string;
  evidenceLinks: CitationDto[];
  targetTeam: string;
  reason: string;
}

export interface UpdateEscalationDraftRequest {
  title?: string;
  customerSummary?: string;
  stepsToReproduce?: string;
  logsIdsRequested?: string;
  suspectedComponent?: string;
  severity?: string;
  evidenceLinks?: CitationDto[];
  targetTeam?: string;
  reason?: string;
}

export interface EscalationDraftResponse {
  draftId: string;
  sessionId: string;
  messageId: string;
  title: string;
  customerSummary: string;
  stepsToReproduce: string;
  logsIdsRequested: string;
  suspectedComponent: string;
  severity: string;
  evidenceLinks: CitationDto[];
  targetTeam: string;
  reason: string;
  createdAt: string;
  exportedAt: string | null;
}

export interface EscalationDraftListResponse {
  sessionId: string;
  drafts: EscalationDraftResponse[];
  totalCount: number;
}

export interface EscalationDraftExportResponse {
  draftId: string;
  markdown: string;
  exportedAt: string;
}

// ── Feedback types ──

export type FeedbackType = 'ThumbsUp' | 'ThumbsDown';

export type FeedbackReasonCode =
  | 'WrongAnswer'
  | 'OutdatedInfo'
  | 'MissingContext'
  | 'WrongSource'
  | 'TooVague'
  | 'WrongEscalation'
  | 'Other';

export interface SubmitFeedbackRequest {
  type: FeedbackType;
  reasonCodes: FeedbackReasonCode[];
  comment?: string;
  correctionText?: string;
  correctedAnswer?: string;
}

export interface FeedbackResponse {
  feedbackId: string;
  messageId: string;
  sessionId: string;
  type: string;
  reasonCodes: string[];
  comment: string | null;
  correctionText: string | null;
  correctedAnswer: string | null;
  traceId: string | null;
  createdAt: string;
}
