using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Audit;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Audit;

public class InMemoryAuditEventWriterTests
{
    [Fact]
    public async Task WriteAsync_StoresEvent()
    {
        var writer = new InMemoryAuditEventWriter(NullLogger<InMemoryAuditEventWriter>.Instance);
        var evt = new AuditEvent("e1", "test.event", "t1", "u1", "c1", DateTimeOffset.UtcNow, "detail");

        await writer.WriteAsync(evt);

        var events = writer.GetEvents();
        Assert.Single(events);
        Assert.Equal("test.event", events[0].EventType);
    }

    [Fact]
    public async Task WriteAsync_StoresMultipleEvents()
    {
        var writer = new InMemoryAuditEventWriter(NullLogger<InMemoryAuditEventWriter>.Instance);

        await writer.WriteAsync(new AuditEvent("e1", "type1", "t1", "u1", "c1", DateTimeOffset.UtcNow, "d1"));
        await writer.WriteAsync(new AuditEvent("e2", "type2", "t1", "u1", "c1", DateTimeOffset.UtcNow, "d2"));

        Assert.Equal(2, writer.GetEvents().Count);
    }

    [Fact]
    public void GetEvents_ReturnsEmpty_WhenNoEventsWritten()
    {
        var writer = new InMemoryAuditEventWriter(NullLogger<InMemoryAuditEventWriter>.Instance);
        Assert.Empty(writer.GetEvents());
    }
}
