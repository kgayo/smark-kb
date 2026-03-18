using Microsoft.EntityFrameworkCore;
using SmartKb.Data.Entities;

namespace SmartKb.Data;

public class SmartKbDbContext : DbContext
{
    public SmartKbDbContext(DbContextOptions<SmartKbDbContext> options) : base(options) { }

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<UserRoleMappingEntity> UserRoleMappings => Set<UserRoleMappingEntity>();
    public DbSet<ConnectorEntity> Connectors => Set<ConnectorEntity>();
    public DbSet<SyncRunEntity> SyncRuns => Set<SyncRunEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<FeedbackEntity> Feedbacks => Set<FeedbackEntity>();
    public DbSet<OutcomeEventEntity> OutcomeEvents => Set<OutcomeEventEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<RetentionConfigEntity> RetentionConfigs => Set<RetentionConfigEntity>();
    public DbSet<WebhookSubscriptionEntity> WebhookSubscriptions => Set<WebhookSubscriptionEntity>();
    public DbSet<EvidenceChunkEntity> EvidenceChunks => Set<EvidenceChunkEntity>();
    public DbSet<RawContentSnapshotEntity> RawContentSnapshots => Set<RawContentSnapshotEntity>();
    public DbSet<AnswerTraceEntity> AnswerTraces => Set<AnswerTraceEntity>();
    public DbSet<EscalationDraftEntity> EscalationDrafts => Set<EscalationDraftEntity>();
    public DbSet<EscalationRoutingRuleEntity> EscalationRoutingRules => Set<EscalationRoutingRuleEntity>();
    public DbSet<CasePatternEntity> CasePatterns => Set<CasePatternEntity>();
    public DbSet<TenantRetrievalSettingsEntity> TenantRetrievalSettings => Set<TenantRetrievalSettingsEntity>();
    public DbSet<RoutingRecommendationEntity> RoutingRecommendations => Set<RoutingRecommendationEntity>();
    public DbSet<PiiPolicyEntity> PiiPolicies => Set<PiiPolicyEntity>();
    public DbSet<DataSubjectDeletionRequestEntity> DataSubjectDeletionRequests => Set<DataSubjectDeletionRequestEntity>();
    public DbSet<TeamPlaybookEntity> TeamPlaybooks => Set<TeamPlaybookEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureTenant(modelBuilder);
        ConfigureUserRoleMapping(modelBuilder);
        ConfigureConnector(modelBuilder);
        ConfigureSyncRun(modelBuilder);
        ConfigureSession(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureFeedback(modelBuilder);
        ConfigureOutcomeEvent(modelBuilder);
        ConfigureAuditEvent(modelBuilder);
        ConfigureRetentionConfig(modelBuilder);
        ConfigureWebhookSubscription(modelBuilder);
        ConfigureEvidenceChunk(modelBuilder);
        ConfigureRawContentSnapshot(modelBuilder);
        ConfigureAnswerTrace(modelBuilder);
        ConfigureEscalationDraft(modelBuilder);
        ConfigureEscalationRoutingRule(modelBuilder);
        ConfigureCasePattern(modelBuilder);
        ConfigureTenantRetrievalSettings(modelBuilder);
        ConfigureRoutingRecommendation(modelBuilder);
        ConfigurePiiPolicy(modelBuilder);
        ConfigureDataSubjectDeletionRequest(modelBuilder);
        ConfigureTeamPlaybook(modelBuilder);
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantEntity>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(t => t.TenantId);
            e.Property(t => t.TenantId).HasMaxLength(128);
            e.Property(t => t.DisplayName).HasMaxLength(256).IsRequired();
            e.Property(t => t.IsActive).HasDefaultValue(true);
        });
    }

    private static void ConfigureUserRoleMapping(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRoleMappingEntity>(e =>
        {
            e.ToTable("UserRoleMappings");
            e.HasKey(u => u.Id);
            e.Property(u => u.TenantId).HasMaxLength(128).IsRequired();
            e.Property(u => u.UserId).HasMaxLength(128).IsRequired();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(64).IsRequired();
            e.HasIndex(u => new { u.TenantId, u.UserId, u.Role }).IsUnique();
            e.HasOne(u => u.Tenant).WithMany(t => t.UserRoleMappings).HasForeignKey(u => u.TenantId);
        });
    }

    private static void ConfigureConnector(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConnectorEntity>(e =>
        {
            e.ToTable("Connectors");
            e.HasKey(c => c.Id);
            e.Property(c => c.TenantId).HasMaxLength(128).IsRequired();
            e.Property(c => c.Name).HasMaxLength(256).IsRequired();
            e.Property(c => c.ConnectorType).HasConversion<string>().HasMaxLength(64).IsRequired();
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(c => c.AuthType).HasConversion<string>().HasMaxLength(64).IsRequired();
            e.Property(c => c.KeyVaultSecretName).HasMaxLength(256);
            e.HasIndex(c => c.TenantId);
            e.HasIndex(c => new { c.TenantId, c.Name }).IsUnique().HasFilter("[DeletedAt] IS NULL");
            e.HasQueryFilter(c => c.DeletedAt == null);
            e.HasOne(c => c.Tenant).WithMany(t => t.Connectors).HasForeignKey(c => c.TenantId);
        });
    }

    private static void ConfigureSyncRun(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncRunEntity>(e =>
        {
            e.ToTable("SyncRuns");
            e.HasKey(s => s.Id);
            e.Property(s => s.TenantId).HasMaxLength(128).IsRequired();
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(s => s.IdempotencyKey).HasMaxLength(256);
            e.HasIndex(s => s.TenantId);
            e.HasIndex(s => s.ConnectorId);
            e.HasIndex(s => s.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            e.HasOne(s => s.Connector).WithMany(c => c.SyncRuns).HasForeignKey(s => s.ConnectorId);
        });
    }

    private static void ConfigureSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.ToTable("Sessions");
            e.HasKey(s => s.Id);
            e.Property(s => s.TenantId).HasMaxLength(128).IsRequired();
            e.Property(s => s.UserId).HasMaxLength(128).IsRequired();
            e.Property(s => s.Title).HasMaxLength(512);
            e.Property(s => s.CustomerRef).HasMaxLength(256);
            e.HasIndex(s => s.TenantId);
            e.HasIndex(s => new { s.TenantId, s.UserId });
            e.HasQueryFilter(s => s.DeletedAt == null);
            e.HasOne(s => s.Tenant).WithMany(t => t.Sessions).HasForeignKey(s => s.TenantId);
        });
    }

    private static void ConfigureMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.TenantId).HasMaxLength(128).IsRequired();
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(m => m.Content).IsRequired();
            e.Property(m => m.CitationsJson);
            e.Property(m => m.Confidence);
            e.Property(m => m.ConfidenceLabel).HasMaxLength(32);
            e.Property(m => m.ResponseType).HasMaxLength(64);
            e.Property(m => m.TraceId).HasMaxLength(128);
            e.Property(m => m.CorrelationId).HasMaxLength(128);
            e.HasIndex(m => m.SessionId);
            e.HasIndex(m => m.TenantId);
            e.HasQueryFilter(m => m.DeletedAt == null);
            e.HasOne(m => m.Session).WithMany(s => s.Messages).HasForeignKey(m => m.SessionId);
        });
    }

    private static void ConfigureFeedback(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeedbackEntity>(e =>
        {
            e.ToTable("Feedbacks");
            e.HasKey(f => f.Id);
            e.Property(f => f.TenantId).HasMaxLength(128).IsRequired();
            e.Property(f => f.UserId).HasMaxLength(128).IsRequired();
            e.Property(f => f.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(f => f.ReasonCodesJson);
            e.Property(f => f.Comment);
            e.Property(f => f.TraceId).HasMaxLength(128);
            e.Property(f => f.CorrelationId).HasMaxLength(128);
            e.HasIndex(f => f.TenantId);
            e.HasIndex(f => f.SessionId);
            e.HasIndex(f => f.MessageId);
            e.HasIndex(f => new { f.TenantId, f.SessionId });
            e.HasOne(f => f.Session).WithMany(s => s.Feedbacks).HasForeignKey(f => f.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.Message).WithMany().HasForeignKey(f => f.MessageId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOutcomeEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutcomeEventEntity>(e =>
        {
            e.ToTable("OutcomeEvents");
            e.HasKey(o => o.Id);
            e.Property(o => o.TenantId).HasMaxLength(128).IsRequired();
            e.Property(o => o.ResolutionType).HasConversion<string>().HasMaxLength(64).IsRequired();
            e.Property(o => o.TargetTeam).HasMaxLength(256);
            e.Property(o => o.EscalationTraceId).HasMaxLength(128);
            e.HasIndex(o => o.TenantId);
            e.HasIndex(o => o.SessionId);
            e.HasOne(o => o.Session).WithMany(s => s.OutcomeEvents).HasForeignKey(o => o.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAuditEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventEntity>(e =>
        {
            e.ToTable("AuditEvents");
            e.HasKey(a => a.Id);
            e.Property(a => a.EventType).HasMaxLength(128).IsRequired();
            e.Property(a => a.TenantId).HasMaxLength(128).IsRequired();
            e.Property(a => a.ActorId).HasMaxLength(128).IsRequired();
            e.Property(a => a.CorrelationId).HasMaxLength(128).IsRequired();
            e.Property(a => a.Detail).IsRequired();
            e.HasIndex(a => a.TenantId);
            e.HasIndex(a => a.EventType);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.TenantId, a.Timestamp });
            e.HasIndex(a => new { a.TenantId, a.EventType });
        });
    }

    private static void ConfigureRetentionConfig(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RetentionConfigEntity>(e =>
        {
            e.ToTable("RetentionConfigs");
            e.HasKey(r => r.Id);
            e.Property(r => r.TenantId).HasMaxLength(128).IsRequired();
            e.Property(r => r.EntityType).HasMaxLength(128).IsRequired();
            e.HasIndex(r => new { r.TenantId, r.EntityType }).IsUnique();
            e.HasOne(r => r.Tenant).WithMany(t => t.RetentionConfigs).HasForeignKey(r => r.TenantId);
        });
    }

    private static void ConfigureWebhookSubscription(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookSubscriptionEntity>(e =>
        {
            e.ToTable("WebhookSubscriptions");
            e.HasKey(w => w.Id);
            e.Property(w => w.TenantId).HasMaxLength(128).IsRequired();
            e.Property(w => w.ExternalSubscriptionId).HasMaxLength(256);
            e.Property(w => w.EventType).HasMaxLength(128).IsRequired();
            e.Property(w => w.CallbackUrl).HasMaxLength(1024).IsRequired();
            e.Property(w => w.WebhookSecretName).HasMaxLength(256);
            e.HasIndex(w => w.ConnectorId);
            e.HasIndex(w => w.TenantId);
            e.HasIndex(w => new { w.ConnectorId, w.EventType }).IsUnique();
            e.HasOne(w => w.Connector).WithMany(c => c.WebhookSubscriptions).HasForeignKey(w => w.ConnectorId);
        });
    }

    private static void ConfigureEvidenceChunk(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidenceChunkEntity>(e =>
        {
            e.ToTable("EvidenceChunks");
            e.HasKey(c => c.ChunkId);
            e.Property(c => c.ChunkId).HasMaxLength(512);
            e.Property(c => c.EvidenceId).HasMaxLength(256).IsRequired();
            e.Property(c => c.TenantId).HasMaxLength(128).IsRequired();
            e.Property(c => c.ChunkText).IsRequired();
            e.Property(c => c.ChunkContext).HasMaxLength(1024);
            e.Property(c => c.SourceSystem).HasMaxLength(64).IsRequired();
            e.Property(c => c.SourceType).HasMaxLength(64).IsRequired();
            e.Property(c => c.Status).HasMaxLength(32).IsRequired();
            e.Property(c => c.Visibility).HasMaxLength(32).IsRequired();
            e.Property(c => c.AccessLabel).HasMaxLength(256).IsRequired();
            e.Property(c => c.Title).HasMaxLength(512).IsRequired();
            e.Property(c => c.SourceUrl).HasMaxLength(2048).IsRequired();
            e.Property(c => c.ContentHash).HasMaxLength(128).IsRequired();
            e.Property(c => c.ProductArea).HasMaxLength(256);
            e.HasIndex(c => c.TenantId);
            e.HasIndex(c => c.EvidenceId);
            e.HasIndex(c => c.ConnectorId);
            e.HasIndex(c => new { c.TenantId, c.EvidenceId });
            e.HasIndex(c => new { c.EvidenceId, c.ContentHash });
            e.HasOne(c => c.Connector).WithMany().HasForeignKey(c => c.ConnectorId);
        });
    }

    private static void ConfigureRawContentSnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawContentSnapshotEntity>(e =>
        {
            e.ToTable("RawContentSnapshots");
            e.HasKey(r => r.EvidenceId);
            e.Property(r => r.EvidenceId).HasMaxLength(256);
            e.Property(r => r.TenantId).HasMaxLength(128).IsRequired();
            e.Property(r => r.BlobPath).HasMaxLength(1024).IsRequired();
            e.Property(r => r.ContentHash).HasMaxLength(128).IsRequired();
            e.Property(r => r.ContentType).HasMaxLength(128).IsRequired();
            e.HasIndex(r => r.TenantId);
            e.HasIndex(r => r.ConnectorId);
            e.HasIndex(r => new { r.TenantId, r.ConnectorId });
            e.HasOne(r => r.Connector).WithMany().HasForeignKey(r => r.ConnectorId);
        });
    }

    private static void ConfigureAnswerTrace(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnswerTraceEntity>(e =>
        {
            e.ToTable("AnswerTraces");
            e.HasKey(a => a.Id);
            e.Property(a => a.TenantId).HasMaxLength(128).IsRequired();
            e.Property(a => a.UserId).HasMaxLength(128).IsRequired();
            e.Property(a => a.CorrelationId).HasMaxLength(128).IsRequired();
            e.Property(a => a.Query).IsRequired();
            e.Property(a => a.ResponseType).HasMaxLength(64).IsRequired();
            e.Property(a => a.ConfidenceLabel).HasMaxLength(32).IsRequired();
            e.Property(a => a.CitedChunkIds).IsRequired();
            e.Property(a => a.RetrievedChunkIds).IsRequired();
            e.Property(a => a.SystemPromptVersion).HasMaxLength(32).IsRequired();
            e.HasIndex(a => a.TenantId);
            e.HasIndex(a => a.CorrelationId);
            e.HasIndex(a => new { a.TenantId, a.CreatedAt });
        });
    }

    private static void ConfigureEscalationDraft(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EscalationDraftEntity>(e =>
        {
            e.ToTable("EscalationDrafts");
            e.HasKey(d => d.Id);
            e.Property(d => d.TenantId).HasMaxLength(128).IsRequired();
            e.Property(d => d.UserId).HasMaxLength(128).IsRequired();
            e.Property(d => d.Title).HasMaxLength(512).IsRequired();
            e.Property(d => d.CustomerSummary).IsRequired();
            e.Property(d => d.StepsToReproduce).IsRequired();
            e.Property(d => d.LogsIdsRequested).IsRequired();
            e.Property(d => d.SuspectedComponent).HasMaxLength(256).IsRequired();
            e.Property(d => d.Severity).HasMaxLength(16).IsRequired();
            e.Property(d => d.EvidenceLinksJson).IsRequired();
            e.Property(d => d.TargetTeam).HasMaxLength(256).IsRequired();
            e.Property(d => d.Reason).IsRequired();
            e.HasIndex(d => d.TenantId);
            e.HasIndex(d => d.SessionId);
            e.HasIndex(d => new { d.TenantId, d.SessionId, d.CreatedAt });
            e.HasIndex(d => new { d.TenantId, d.UserId, d.CreatedAt });
            e.Property(d => d.ApprovedBy).HasMaxLength(128);
            e.Property(d => d.TargetConnectorType).HasConversion<string>().HasMaxLength(64);
            e.Property(d => d.ExternalId).HasMaxLength(512);
            e.Property(d => d.ExternalUrl).HasMaxLength(2048);
            e.Property(d => d.ExternalStatus).HasMaxLength(32);
            e.HasQueryFilter(d => d.DeletedAt == null);
            e.HasOne(d => d.Session).WithMany(s => s.EscalationDrafts).HasForeignKey(d => d.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.TargetConnector).WithMany().HasForeignKey(d => d.TargetConnectorId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureEscalationRoutingRule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EscalationRoutingRuleEntity>(e =>
        {
            e.ToTable("EscalationRoutingRules");
            e.HasKey(r => r.Id);
            e.Property(r => r.TenantId).HasMaxLength(128).IsRequired();
            e.Property(r => r.ProductArea).HasMaxLength(256).IsRequired();
            e.Property(r => r.TargetTeam).HasMaxLength(256).IsRequired();
            e.Property(r => r.MinSeverity).HasMaxLength(16).IsRequired();
            e.HasIndex(r => r.TenantId);
            e.HasIndex(r => new { r.TenantId, r.ProductArea }).IsUnique().HasFilter("[IsActive] = 1");
            e.HasOne(r => r.Tenant).WithMany().HasForeignKey(r => r.TenantId);
        });
    }

    private static void ConfigureCasePattern(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CasePatternEntity>(e =>
        {
            e.ToTable("CasePatterns");
            e.HasKey(p => p.Id);
            e.Property(p => p.PatternId).HasMaxLength(256).IsRequired();
            e.Property(p => p.TenantId).HasMaxLength(128).IsRequired();
            e.Property(p => p.Title).HasMaxLength(512).IsRequired();
            e.Property(p => p.ProblemStatement).IsRequired();
            e.Property(p => p.SymptomsJson).IsRequired();
            e.Property(p => p.DiagnosisStepsJson).IsRequired();
            e.Property(p => p.ResolutionStepsJson).IsRequired();
            e.Property(p => p.VerificationStepsJson).IsRequired();
            e.Property(p => p.EscalationCriteriaJson).IsRequired();
            e.Property(p => p.EscalationTargetTeam).HasMaxLength(256);
            e.Property(p => p.RelatedEvidenceIdsJson).IsRequired();
            e.Property(p => p.TrustLevel).HasMaxLength(32).IsRequired();
            e.Property(p => p.Version).HasDefaultValue(1);
            e.Property(p => p.SupersedesPatternId).HasMaxLength(256);
            e.Property(p => p.ApplicabilityConstraintsJson).IsRequired();
            e.Property(p => p.ExclusionsJson).IsRequired();
            e.Property(p => p.ProductArea).HasMaxLength(256);
            e.Property(p => p.TagsJson).IsRequired();
            e.Property(p => p.Visibility).HasMaxLength(32).IsRequired();
            e.Property(p => p.AllowedGroupsJson).IsRequired();
            e.Property(p => p.AccessLabel).HasMaxLength(256).IsRequired();
            e.Property(p => p.SourceUrl).HasMaxLength(2048).IsRequired();
            e.HasIndex(p => p.TenantId);
            e.HasIndex(p => p.PatternId).IsUnique().HasFilter("[DeletedAt] IS NULL");
            e.HasIndex(p => new { p.TenantId, p.TrustLevel });
            e.HasIndex(p => new { p.TenantId, p.ProductArea });
            e.HasIndex(p => new { p.TenantId, p.UpdatedAt });
            // Governance tracking (P1-006).
            e.Property(p => p.ReviewedBy).HasMaxLength(128);
            e.Property(p => p.ReviewNotes).HasMaxLength(1024);
            e.Property(p => p.ApprovedBy).HasMaxLength(128);
            e.Property(p => p.ApprovalNotes).HasMaxLength(1024);
            e.Property(p => p.DeprecatedBy).HasMaxLength(128);
            e.Property(p => p.DeprecationReason).HasMaxLength(1024);
            // Quality gate score (P1-011).
            e.Property(p => p.QualityScore);

            e.HasQueryFilter(p => p.DeletedAt == null);
            e.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTenantRetrievalSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantRetrievalSettingsEntity>(e =>
        {
            e.ToTable("TenantRetrievalSettings");
            e.HasKey(s => s.Id);
            e.Property(s => s.TenantId).HasMaxLength(128).IsRequired();
            e.HasIndex(s => s.TenantId).IsUnique();
            e.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId);
        });
    }

    private static void ConfigureRoutingRecommendation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoutingRecommendationEntity>(e =>
        {
            e.ToTable("RoutingRecommendations");
            e.HasKey(r => r.Id);
            e.Property(r => r.TenantId).HasMaxLength(128).IsRequired();
            e.Property(r => r.RecommendationType).HasMaxLength(64).IsRequired();
            e.Property(r => r.ProductArea).HasMaxLength(256).IsRequired();
            e.Property(r => r.CurrentTargetTeam).HasMaxLength(256).IsRequired();
            e.Property(r => r.SuggestedTargetTeam).HasMaxLength(256);
            e.Property(r => r.Reason).IsRequired();
            e.Property(r => r.Status).HasMaxLength(32).IsRequired();
            e.Property(r => r.AppliedBy).HasMaxLength(128);
            e.Property(r => r.DismissedBy).HasMaxLength(128);
            e.HasIndex(r => r.TenantId);
            e.HasIndex(r => new { r.TenantId, r.Status });
            e.HasIndex(r => new { r.TenantId, r.ProductArea });
            e.HasOne(r => r.Tenant).WithMany().HasForeignKey(r => r.TenantId);
        });
    }

    private static void ConfigurePiiPolicy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PiiPolicyEntity>(e =>
        {
            e.ToTable("PiiPolicies");
            e.HasKey(p => p.Id);
            e.Property(p => p.TenantId).HasMaxLength(128).IsRequired();
            e.Property(p => p.EnforcementMode).HasMaxLength(32).IsRequired();
            e.Property(p => p.EnabledPiiTypes).HasMaxLength(512).IsRequired();
            e.Property(p => p.CustomPatternsJson).IsRequired();
            e.HasIndex(p => p.TenantId).IsUnique();
            e.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId);
        });
    }

    private static void ConfigureDataSubjectDeletionRequest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataSubjectDeletionRequestEntity>(e =>
        {
            e.ToTable("DataSubjectDeletionRequests");
            e.HasKey(d => d.Id);
            e.Property(d => d.TenantId).HasMaxLength(128).IsRequired();
            e.Property(d => d.SubjectId).HasMaxLength(128).IsRequired();
            e.Property(d => d.RequestedBy).HasMaxLength(128).IsRequired();
            e.Property(d => d.Status).HasMaxLength(32).IsRequired();
            e.Property(d => d.DeletionSummaryJson).IsRequired();
            e.Property(d => d.ErrorDetail);
            e.HasIndex(d => d.TenantId);
            e.HasIndex(d => new { d.TenantId, d.SubjectId });
            e.HasIndex(d => new { d.TenantId, d.Status });
            e.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId);
        });
    }

    private static void ConfigureTeamPlaybook(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamPlaybookEntity>(e =>
        {
            e.ToTable("TeamPlaybooks");
            e.HasKey(p => p.Id);
            e.Property(p => p.TenantId).HasMaxLength(128).IsRequired();
            e.Property(p => p.TeamName).HasMaxLength(256).IsRequired();
            e.Property(p => p.Description).HasMaxLength(1024).IsRequired();
            e.Property(p => p.RequiredFieldsJson).IsRequired();
            e.Property(p => p.ChecklistJson).IsRequired();
            e.Property(p => p.ContactChannel).HasMaxLength(512);
            e.Property(p => p.MinSeverity).HasMaxLength(16);
            e.Property(p => p.AutoRouteSeverity).HasMaxLength(16);
            e.Property(p => p.FallbackTeam).HasMaxLength(256);
            e.HasIndex(p => p.TenantId);
            e.HasIndex(p => new { p.TenantId, p.TeamName }).IsUnique().HasFilter("[DeletedAt] IS NULL");
            e.HasQueryFilter(p => p.DeletedAt == null);
            e.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId);
        });
    }
}
