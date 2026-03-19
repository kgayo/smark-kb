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

export interface RetrievalFilter {
  sourceTypes?: string[];
  productAreas?: string[];
  timeHorizonDays?: number;
  tags?: string[];
  statuses?: string[];
}

export interface SendMessageRequest {
  query: string;
  userGroups?: string[];
  maxCitations?: number;
  filters?: RetrievalFilter;
}

export interface MessageResponse {
  messageId: string;
  sessionId: string;
  role: 'user' | 'assistant';
  content: string;
  citations: CitationDto[] | null;
  confidence: number | null;
  confidenceLabel: string | null;
  confidenceRationale: string | null;
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
  confidenceRationale: string | null;
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
  rootCause: string | null;
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

// ── Pattern usage metrics types (P3-012) ──

export interface PatternUsageMetrics {
  patternId: string;
  totalCitations: number;
  citationsLast7Days: number;
  citationsLast30Days: number;
  citationsLast90Days: number;
  uniqueUsers: number;
  averageConfidence: number;
  lastCitedAt: string | null;
  firstCitedAt: string | null;
  dailyBreakdown: PatternUsageDayBucket[];
}

export interface PatternUsageDayBucket {
  date: string;
  citations: number;
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

// ── Retrieval tuning types (P1-007) ──

export interface RetrievalSettingsResponse {
  tenantId: string;
  topK: number;
  enableSemanticReranking: boolean;
  enablePatternFusion: boolean;
  patternTopK: number;
  trustBoostApproved: number;
  trustBoostReviewed: number;
  trustBoostDraft: number;
  recencyBoostRecent: number;
  recencyBoostOld: number;
  patternAuthorityBoost: number;
  diversityMaxPerSource: number;
  noEvidenceScoreThreshold: number;
  noEvidenceMinResults: number;
  hasOverrides: boolean;
}

export interface UpdateRetrievalSettingsRequest {
  topK?: number;
  enableSemanticReranking?: boolean;
  enablePatternFusion?: boolean;
  patternTopK?: number;
  trustBoostApproved?: number;
  trustBoostReviewed?: number;
  trustBoostDraft?: number;
  recencyBoostRecent?: number;
  recencyBoostOld?: number;
  patternAuthorityBoost?: number;
  diversityMaxPerSource?: number;
  noEvidenceScoreThreshold?: number;
  noEvidenceMinResults?: number;
}

// ── Diagnostics types (P1-008) ──

export interface WebhookSubscriptionStatus {
  id: string;
  connectorId: string;
  connectorName: string;
  connectorType: string;
  eventType: string;
  isActive: boolean;
  pollingFallbackActive: boolean;
  consecutiveFailures: number;
  lastDeliveryAt: string | null;
  nextPollAt: string | null;
  externalSubscriptionId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface WebhookStatusListResponse {
  subscriptions: WebhookSubscriptionStatus[];
  totalCount: number;
  activeCount: number;
  fallbackCount: number;
}

export interface ConnectorHealthSummary {
  connectorId: string;
  name: string;
  connectorType: string;
  status: string;
  lastSyncStatus: string | null;
  lastSyncAt: string | null;
  webhookCount: number;
  webhooksInFallback: number;
  totalFailures: number;
}

export interface DiagnosticsSummaryResponse {
  totalConnectors: number;
  enabledConnectors: number;
  disabledConnectors: number;
  totalWebhooks: number;
  activeWebhooks: number;
  fallbackWebhooks: number;
  failingWebhooks: number;
  serviceBusConfigured: boolean;
  keyVaultConfigured: boolean;
  openAiConfigured: boolean;
  searchServiceConfigured: boolean;
  connectorHealth: ConnectorHealthSummary[];
  credentialWarnings: number;
  credentialCritical: number;
  credentialExpired: number;
}

export interface DeadLetterMessage {
  messageId: string;
  correlationId: string | null;
  subject: string | null;
  deadLetterReason: string | null;
  deadLetterErrorDescription: string | null;
  deliveryCount: number;
  enqueuedTime: string;
  body: string;
  applicationProperties: Record<string, unknown>;
}

export interface DeadLetterListResponse {
  messages: DeadLetterMessage[];
  count: number;
  serviceBusConfigured?: boolean;
}

export interface SloStatusResponse {
  targets: {
    answerLatencyP95TargetMs: number;
    availabilityTargetPercent: number;
    syncLagP95TargetMinutes: number;
    noEvidenceRateThreshold: number;
    deadLetterDepthThreshold: number;
  };
  metrics: Record<string, string>;
  dashboardHint: string;
}

export interface SecretsStatusResponse {
  tenantId: string;
  keyVaultConfigured: boolean;
  openAiKeyConfigured: boolean;
  openAiModel: string;
}

// ── Credential status types (P3-009) ──

export type CredentialHealth = 'Healthy' | 'Warning' | 'Critical' | 'Expired' | 'Missing' | 'Unknown';

export interface ConnectorCredentialStatus {
  connectorId: string;
  connectorName: string;
  connectorType: string;
  authType: string;
  health: CredentialHealth;
  secretName: string | null;
  createdOn: string | null;
  expiresOn: string | null;
  daysUntilExpiry: number | null;
  ageDays: number | null;
  message: string | null;
}

export interface CredentialStatusSummary {
  connectors: ConnectorCredentialStatus[];
  totalConnectors: number;
  healthyCount: number;
  warningCount: number;
  criticalCount: number;
  expiredCount: number;
  missingCount: number;
}

export interface CredentialRotationResult {
  success: boolean;
  message: string;
  newSecretCreatedOn: string | null;
}

// ── Synonym map types (P3-004) ──

export interface SynonymRuleResponse {
  id: string;
  tenantId: string;
  groupName: string;
  rule: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
  updatedBy: string | null;
}

export interface SynonymRuleListResponse {
  rules: SynonymRuleResponse[];
  totalCount: number;
  groups: string[];
}

export interface CreateSynonymRuleRequest {
  rule: string;
  groupName?: string;
  description?: string;
}

export interface UpdateSynonymRuleRequest {
  rule?: string;
  groupName?: string;
  description?: string;
  isActive?: boolean;
}

export interface SynonymMapSyncResult {
  success: boolean;
  ruleCount: number;
  evidenceSynonymMapName: string;
  patternSynonymMapName: string;
  errorDetail: string | null;
}

export interface SynonymRuleValidationResult {
  isValid: boolean;
  errors: string[];
}

// ── Routing analytics types (P1-009) ──

export interface RoutingRuleDto {
  ruleId: string;
  productArea: string;
  targetTeam: string;
  escalationThreshold: number;
  minSeverity: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface RoutingRuleListResponse {
  rules: RoutingRuleDto[];
  totalCount: number;
}

export interface CreateRoutingRuleRequest {
  productArea: string;
  targetTeam: string;
  escalationThreshold?: number;
  minSeverity?: string;
}

export interface UpdateRoutingRuleRequest {
  targetTeam?: string;
  escalationThreshold?: number;
  minSeverity?: string;
  isActive?: boolean;
}

export interface TeamRoutingMetrics {
  targetTeam: string;
  totalEscalations: number;
  acceptedCount: number;
  reroutedCount: number;
  pendingCount: number;
  acceptanceRate: number;
  rerouteRate: number;
  avgTimeToAssign: string | null;
  avgTimeToResolve: string | null;
}

export interface ProductAreaRoutingMetrics {
  productArea: string;
  currentTargetTeam: string;
  totalEscalations: number;
  acceptedCount: number;
  reroutedCount: number;
  acceptanceRate: number;
  rerouteRate: number;
}

export interface RoutingAnalyticsSummary {
  tenantId: string;
  totalOutcomes: number;
  totalEscalations: number;
  totalReroutes: number;
  totalResolvedWithoutEscalation: number;
  overallAcceptanceRate: number;
  overallRerouteRate: number;
  selfResolutionRate: number;
  teamMetrics: TeamRoutingMetrics[];
  productAreaMetrics: ProductAreaRoutingMetrics[];
  computedAt: string;
  windowStart: string | null;
  windowEnd: string | null;
}

export interface RoutingRecommendationDto {
  recommendationId: string;
  recommendationType: string;
  productArea: string;
  currentTargetTeam: string;
  suggestedTargetTeam: string | null;
  currentThreshold: number | null;
  suggestedThreshold: number | null;
  reason: string;
  confidence: number;
  supportingOutcomeCount: number;
  status: string;
  createdAt: string;
  appliedAt: string | null;
  appliedBy: string | null;
}

export interface RoutingRecommendationListResponse {
  recommendations: RoutingRecommendationDto[];
  totalCount: number;
}

export interface ApplyRecommendationRequest {
  overrideTargetTeam?: string;
  overrideThreshold?: number;
}

// ── Team playbook types (P2-002) ──

export interface TeamPlaybookDto {
  id: string;
  teamName: string;
  description: string;
  requiredFields: string[];
  checklist: string[];
  contactChannel: string | null;
  requiresApproval: boolean;
  minSeverity: string | null;
  autoRouteSeverity: string | null;
  maxConcurrentEscalations: number | null;
  fallbackTeam: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TeamPlaybookListResponse {
  playbooks: TeamPlaybookDto[];
  totalCount: number;
}

export interface CreateTeamPlaybookRequest {
  teamName: string;
  description?: string;
  requiredFields?: string[];
  checklist?: string[];
  contactChannel?: string;
  requiresApproval?: boolean;
  minSeverity?: string;
  autoRouteSeverity?: string;
  maxConcurrentEscalations?: number;
  fallbackTeam?: string;
}

export interface UpdateTeamPlaybookRequest {
  description?: string;
  requiredFields?: string[];
  checklist?: string[];
  contactChannel?: string;
  requiresApproval?: boolean;
  minSeverity?: string;
  autoRouteSeverity?: string;
  maxConcurrentEscalations?: number;
  fallbackTeam?: string;
  isActive?: boolean;
}

// ── Cost controls types (P2-003) ──

export interface CostSettingsResponse {
  tenantId: string;
  dailyTokenBudget: number | null;
  monthlyTokenBudget: number | null;
  maxPromptTokensPerQuery: number | null;
  maxEvidenceChunksInPrompt: number;
  enableEmbeddingCache: boolean;
  embeddingCacheTtlHours: number;
  enableRetrievalCompression: boolean;
  maxChunkCharsCompressed: number;
  budgetAlertThresholdPercent: number;
  hasOverrides: boolean;
}

export interface UpdateCostSettingsRequest {
  dailyTokenBudget?: number | null;
  monthlyTokenBudget?: number | null;
  maxPromptTokensPerQuery?: number | null;
  maxEvidenceChunksInPrompt?: number;
  enableEmbeddingCache?: boolean;
  embeddingCacheTtlHours?: number;
  enableRetrievalCompression?: boolean;
  maxChunkCharsCompressed?: number;
  budgetAlertThresholdPercent?: number;
}

export interface TokenUsageSummary {
  tenantId: string;
  periodStart: string;
  periodEnd: string;
  totalPromptTokens: number;
  totalCompletionTokens: number;
  totalTokens: number;
  totalEmbeddingTokens: number;
  totalRequests: number;
  embeddingCacheHits: number;
  embeddingCacheMisses: number;
  totalEstimatedCostUsd: number;
  dailyTokenBudget: number | null;
  monthlyTokenBudget: number | null;
  dailyBudgetUtilizationPercent: number;
  monthlyBudgetUtilizationPercent: number;
}

export interface DailyUsageBreakdown {
  date: string;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  embeddingTokens: number;
  requestCount: number;
  cacheHits: number;
  estimatedCostUsd: number;
}

export interface BudgetCheckResult {
  allowed: boolean;
  denialReason: string | null;
  dailyUtilizationPercent: number;
  monthlyUtilizationPercent: number;
  budgetWarning: boolean;
  warningMessage: string | null;
}

// ── Privacy admin types (P2-001, P2-005) ──

export interface PiiPolicyResponse {
  tenantId: string;
  enforcementMode: string;
  enabledPiiTypes: string[];
  customPatterns: CustomPiiPattern[];
  auditRedactions: boolean;
  updatedAt: string;
}

export interface PiiPolicyUpdateRequest {
  enforcementMode: string;
  enabledPiiTypes: string[];
  customPatterns?: CustomPiiPattern[];
  auditRedactions?: boolean;
}

export interface CustomPiiPattern {
  name: string;
  pattern: string;
  placeholder: string;
}

export interface RetentionPolicyResponse {
  tenantId: string;
  policies: RetentionPolicyEntry[];
}

export interface RetentionPolicyEntry {
  entityType: string;
  retentionDays: number;
  metricRetentionDays: number | null;
  updatedAt: string;
}

export interface RetentionPolicyUpdateRequest {
  entityType: string;
  retentionDays: number;
  metricRetentionDays?: number;
}

export interface RetentionCleanupResult {
  tenantId: string;
  entityType: string;
  deletedCount: number;
  cutoffDate: string;
  executedAt: string;
}

export interface DataSubjectDeletionRequest {
  subjectId: string;
}

export interface DataSubjectDeletionResponse {
  requestId: string;
  tenantId: string;
  subjectId: string;
  status: string;
  requestedAt: string;
  completedAt: string | null;
  deletionSummary: Record<string, number> | null;
  errorDetail: string | null;
}

export interface DataSubjectDeletionListResponse {
  requests: DataSubjectDeletionResponse[];
  totalCount: number;
}

export interface RetentionExecutionLogEntry {
  id: string;
  tenantId: string;
  entityType: string;
  deletedCount: number;
  cutoffDate: string;
  executedAt: string;
  durationMs: number;
  actorId: string;
}

export interface RetentionExecutionHistoryResponse {
  entries: RetentionExecutionLogEntry[];
  totalCount: number;
}

export interface RetentionComplianceEntry {
  entityType: string;
  retentionDays: number;
  metricRetentionDays: number | null;
  lastExecutedAt: string | null;
  lastDeletedCount: number | null;
  isOverdue: boolean;
  daysSinceLastExecution: number;
}

export interface RetentionComplianceReport {
  tenantId: string;
  generatedAt: string;
  isCompliant: boolean;
  totalPolicies: number;
  overduePolicies: number;
  entries: RetentionComplianceEntry[];
}

// ── Audit & Compliance ──

export interface AuditEventResponse {
  eventId: string;
  eventType: string;
  tenantId: string;
  actorId: string;
  correlationId: string;
  timestamp: string;
  detail: string;
}

export interface AuditEventListResponse {
  events: AuditEventResponse[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface AuditEventQueryParams {
  eventType?: string;
  actorId?: string;
  correlationId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface AuditExportParams {
  eventType?: string;
  actorId?: string;
  from?: string;
  to?: string;
}
