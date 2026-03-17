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
  approvedAt: string | null;
  externalId: string | null;
  externalUrl: string | null;
  externalStatus: string | null;
  externalErrorDetail: string | null;
  targetConnectorType: string | null;
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

export interface ApproveEscalationDraftRequest {
  connectorId: string;
  targetProject?: string;
  targetListId?: string;
  areaPath?: string;
  workItemType?: string;
}

export interface ExternalEscalationResult {
  draftId: string;
  externalStatus: string;
  externalId: string | null;
  externalUrl: string | null;
  errorDetail: string | null;
  approvedAt: string | null;
  connectorType: string | null;
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

// ── Outcome types ──

export type ResolutionType = 'ResolvedWithoutEscalation' | 'Escalated' | 'Rerouted';

export interface RecordOutcomeRequest {
  resolutionType: ResolutionType;
  targetTeam?: string;
  acceptance?: boolean;
  timeToAssign?: string;
  timeToResolve?: string;
  escalationTraceId?: string;
}

export interface OutcomeResponse {
  outcomeId: string;
  sessionId: string;
  resolutionType: string;
  targetTeam: string | null;
  acceptance: boolean | null;
  timeToAssign: string | null;
  timeToResolve: string | null;
  escalationTraceId: string | null;
  createdAt: string;
}

export interface OutcomeListResponse {
  sessionId: string;
  outcomes: OutcomeResponse[];
  totalCount: number;
}

// ── Connector admin types ──

export type ConnectorType = 'AzureDevOps' | 'SharePoint' | 'HubSpot' | 'ClickUp';
export type ConnectorStatus = 'Enabled' | 'Disabled';
export type SyncRunStatus = 'Pending' | 'Running' | 'Completed' | 'Failed';
export type SecretAuthType = 'OAuth' | 'Pat' | 'PrivateKey' | 'ServiceAccount';
export type FieldTransformType = 'Direct' | 'Template' | 'Regex' | 'Lookup' | 'Constant';

export interface FieldMappingRule {
  sourceField: string;
  targetField: string;
  transform: FieldTransformType;
  transformExpression: string | null;
  isRequired: boolean;
  defaultValue: string | null;
}

export interface FieldMappingConfig {
  rules: FieldMappingRule[];
}

export interface SyncRunSummary {
  id: string;
  status: SyncRunStatus;
  isBackfill: boolean;
  startedAt: string;
  completedAt: string | null;
  recordsProcessed: number;
  recordsFailed: number;
  errorDetail: string | null;
}

export interface ConnectorResponse {
  id: string;
  name: string;
  connectorType: ConnectorType;
  status: ConnectorStatus;
  authType: SecretAuthType;
  hasSecret: boolean;
  sourceConfig: string | null;
  fieldMapping: FieldMappingConfig | null;
  scheduleCron: string | null;
  createdAt: string;
  updatedAt: string;
  lastSyncRun: SyncRunSummary | null;
}

export interface ConnectorListResponse {
  connectors: ConnectorResponse[];
  totalCount: number;
}

export interface SyncRunListResponse {
  syncRuns: SyncRunSummary[];
  totalCount: number;
}

export interface CreateConnectorRequest {
  name: string;
  connectorType: ConnectorType;
  authType: SecretAuthType;
  keyVaultSecretName?: string;
  sourceConfig?: string;
  fieldMapping?: FieldMappingConfig;
  scheduleCron?: string;
}

export interface UpdateConnectorRequest {
  name?: string;
  sourceConfig?: string;
  fieldMapping?: FieldMappingConfig;
  scheduleCron?: string;
  keyVaultSecretName?: string;
  authType?: SecretAuthType;
}

export interface SyncNowRequest {
  isBackfill: boolean;
  idempotencyKey?: string;
}

export interface TestConnectionResponse {
  success: boolean;
  message: string;
  diagnosticDetail: string | null;
}

export interface ConnectorValidationResult {
  isValid: boolean;
  errors: string[];
}

// ── Pattern governance types (P1-006) ──

export type TrustLevel = 'Draft' | 'Reviewed' | 'Approved' | 'Deprecated';

export interface PatternSummary {
  id: string;
  patternId: string;
  title: string;
  problemStatement: string;
  trustLevel: TrustLevel;
  confidence: number;
  version: number;
  productArea: string | null;
  tags: string[];
  supersedesPatternId: string | null;
  sourceUrl: string;
  relatedEvidenceCount: number;
  createdAt: string;
  updatedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  deprecatedBy: string | null;
  deprecatedAt: string | null;
  deprecationReason: string | null;
}

export interface PatternDetail {
  id: string;
  patternId: string;
  tenantId: string;
  title: string;
  problemStatement: string;
  symptoms: string[];
  diagnosisSteps: string[];
  resolutionSteps: string[];
  verificationSteps: string[];
  workaround: string | null;
  escalationCriteria: string[];
  escalationTargetTeam: string | null;
  relatedEvidenceIds: string[];
  confidence: number;
  trustLevel: TrustLevel;
  version: number;
  supersedesPatternId: string | null;
  applicabilityConstraints: string[];
  exclusions: string[];
  productArea: string | null;
  tags: string[];
  visibility: string;
  accessLabel: string;
  sourceUrl: string;
  createdAt: string;
  updatedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  reviewNotes: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  approvalNotes: string | null;
  deprecatedBy: string | null;
  deprecatedAt: string | null;
  deprecationReason: string | null;
}

export interface PatternGovernanceQueueResponse {
  patterns: PatternSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface PatternGovernanceResult {
  patternId: string;
  previousTrustLevel: string;
  newTrustLevel: string;
  transitionedBy: string;
  transitionedAt: string;
}

export interface ReviewPatternRequest {
  notes?: string;
}

export interface ApprovePatternRequest {
  notes?: string;
}

export interface DeprecatePatternRequest {
  reason?: string;
  supersedingPatternId?: string;
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
