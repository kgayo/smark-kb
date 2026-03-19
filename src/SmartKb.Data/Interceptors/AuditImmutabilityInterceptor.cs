using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Interceptors;

/// <summary>
/// Prevents modification or deletion of audit event records at the EF Core level.
/// Throws <see cref="InvalidOperationException"/> if any <see cref="AuditEventEntity"/>
/// is in Modified or Deleted state when SaveChanges is called.
/// </summary>
public sealed class AuditImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ThrowIfAuditEventsModified(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfAuditEventsModified(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ThrowIfAuditEventsModified(DbContext? context)
    {
        if (context is null) return;

        var entries = context.ChangeTracker.Entries<AuditEventEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"Audit events are immutable and cannot be modified. Event ID: {entry.Entity.Id}");
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"Audit events are immutable and cannot be deleted. Event ID: {entry.Entity.Id}");
            }
        }
    }
}
