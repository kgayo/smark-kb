using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class FeedbackServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly FeedbackService _service;
    private readonly StubAuditWriter _auditWriter;

    private static readonly Guid SessionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid MessageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    public FeedbackServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedData();
        _service = new FeedbackService(_db, _auditWriter, NullLogger<FeedbackService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var session = new SessionEntity
        {
            Id = SessionId,
            TenantId = "t1",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);

        var message = new MessageEntity
        {
            Id = MessageId,
            SessionId = SessionId,
            TenantId = "t1",
            Role = MessageRole.Assistant,
            Content = "Here is the answer.",
            TraceId = "trace-abc",
            CorrelationId = "corr-abc",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(message);
        _db.SaveChanges();
    }

    [Fact]
    public async Task SubmitFeedback_ThumbsUp_Persists()
    {
        var request = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsUp,
            ReasonCodes = [],
        };

        var result = await _service.SubmitFeedbackAsync("t1", "u1", "corr-1", SessionId, MessageId, request);

        Assert.Equal("ThumbsUp", result.Type);
        Assert.Equal(MessageId, result.MessageId);
        Assert.Equal(SessionId, result.SessionId);
        Assert.Empty(result.ReasonCodes);
        Assert.Equal("trace-abc", result.TraceId);
        Assert.NotEqual(Guid.Empty, result.FeedbackId);
    }

    [Fact]
    public async Task SubmitFeedback_ThumbsDown_WithReasonCodes()
    {
        var request = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsDown,
            ReasonCodes = [FeedbackReasonCode.WrongAnswer, FeedbackReasonCode.OutdatedInfo],
            Comment = "The answer references an old API version.",
        };

        var result = await _service.SubmitFeedbackAsync("t1", "u1", "corr-2", SessionId, MessageId, request);

        Assert.Equal("ThumbsDown", result.Type);
        Assert.Equal(2, result.ReasonCodes.Count);
        Assert.Contains("WrongAnswer", result.ReasonCodes);
        Assert.Contains("OutdatedInfo", result.ReasonCodes);
        Assert.Equal("The answer references an old API version.", result.Comment);
    }

    [Fact]
    public async Task SubmitFeedback_WithCorrectedAnswer()
    {
        var request = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsDown,
            ReasonCodes = [FeedbackReasonCode.WrongAnswer],
            CorrectedAnswer = "The correct approach is to use OAuth2 client credentials flow.",
        };

        var result = await _service.SubmitFeedbackAsync("t1", "u1", "corr-3", SessionId, MessageId, request);

        Assert.Equal("The correct approach is to use OAuth2 client credentials flow.", result.CorrectedAnswer);
    }

    [Fact]
    public async Task SubmitFeedback_WritesAuditEvent()
    {
        var request = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsDown,
            ReasonCodes = [FeedbackReasonCode.MissingContext],
        };

        await _service.SubmitFeedbackAsync("t1", "u1", "corr-4", SessionId, MessageId, request);

        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.ChatFeedback, _auditWriter.Events[0].EventType);
        Assert.Equal("t1", _auditWriter.Events[0].TenantId);
        Assert.Equal("u1", _auditWriter.Events[0].ActorId);
    }

    [Fact]
    public async Task SubmitFeedback_UpdatesExistingFeedback()
    {
        var firstRequest = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsUp,
            ReasonCodes = [],
        };
        var first = await _service.SubmitFeedbackAsync("t1", "u1", "corr-5", SessionId, MessageId, firstRequest);

        var secondRequest = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsDown,
            ReasonCodes = [FeedbackReasonCode.TooVague],
            Comment = "Changed my mind.",
        };
        var second = await _service.SubmitFeedbackAsync("t1", "u1", "corr-6", SessionId, MessageId, secondRequest);

        Assert.Equal(first.FeedbackId, second.FeedbackId);
        Assert.Equal("ThumbsDown", second.Type);
        Assert.Contains("TooVague", second.ReasonCodes);
        Assert.Equal("Changed my mind.", second.Comment);
    }

    [Fact]
    public async Task SubmitFeedback_Throws_WhenSessionNotFound()
    {
        var request = new SubmitFeedbackRequest { Type = FeedbackType.ThumbsUp };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitFeedbackAsync("t1", "u1", "corr-7", Guid.NewGuid(), MessageId, request));
    }

    [Fact]
    public async Task SubmitFeedback_Throws_WhenMessageNotFound()
    {
        var request = new SubmitFeedbackRequest { Type = FeedbackType.ThumbsUp };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitFeedbackAsync("t1", "u1", "corr-8", SessionId, Guid.NewGuid(), request));
    }

    [Fact]
    public async Task SubmitFeedback_Throws_WhenWrongTenant()
    {
        var request = new SubmitFeedbackRequest { Type = FeedbackType.ThumbsUp };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitFeedbackAsync("other-tenant", "u1", "corr-9", SessionId, MessageId, request));
    }

    [Fact]
    public async Task SubmitFeedback_Throws_WhenWrongUser()
    {
        var request = new SubmitFeedbackRequest { Type = FeedbackType.ThumbsUp };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitFeedbackAsync("t1", "other-user", "corr-10", SessionId, MessageId, request));
    }

    [Fact]
    public async Task GetFeedback_ReturnsExisting()
    {
        var request = new SubmitFeedbackRequest
        {
            Type = FeedbackType.ThumbsDown,
            ReasonCodes = [FeedbackReasonCode.WrongSource],
            Comment = "Sources don't match the question.",
        };
        await _service.SubmitFeedbackAsync("t1", "u1", "corr-11", SessionId, MessageId, request);

        var result = await _service.GetFeedbackAsync("t1", "u1", SessionId, MessageId);

        Assert.NotNull(result);
        Assert.Equal("ThumbsDown", result.Type);
        Assert.Contains("WrongSource", result.ReasonCodes);
    }

    [Fact]
    public async Task GetFeedback_ReturnsNull_WhenNoFeedback()
    {
        var result = await _service.GetFeedbackAsync("t1", "u1", SessionId, MessageId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFeedback_ReturnsNull_WhenWrongSession()
    {
        var result = await _service.GetFeedbackAsync("t1", "u1", Guid.NewGuid(), MessageId);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListFeedbacks_ReturnsSessionFeedbacks()
    {
        // Create second message in same session.
        var msg2Id = Guid.NewGuid();
        _db.Messages.Add(new MessageEntity
        {
            Id = msg2Id,
            SessionId = SessionId,
            TenantId = "t1",
            Role = MessageRole.Assistant,
            Content = "Another answer",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        await _service.SubmitFeedbackAsync("t1", "u1", "c1", SessionId, MessageId,
            new SubmitFeedbackRequest { Type = FeedbackType.ThumbsUp });
        await _service.SubmitFeedbackAsync("t1", "u1", "c2", SessionId, msg2Id,
            new SubmitFeedbackRequest { Type = FeedbackType.ThumbsDown, ReasonCodes = [FeedbackReasonCode.Other] });

        var result = await _service.ListFeedbacksAsync("t1", "u1", SessionId);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task ListFeedbacks_ReturnsNull_WhenSessionNotFound()
    {
        var result = await _service.ListFeedbacksAsync("t1", "u1", Guid.NewGuid());
        Assert.Null(result);
    }

    private sealed class StubAuditWriter : IAuditEventWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
