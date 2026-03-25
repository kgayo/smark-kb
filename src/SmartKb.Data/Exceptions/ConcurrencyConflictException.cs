namespace SmartKb.Data.Exceptions;

/// <summary>
/// Thrown when a concurrent modification is detected during an update operation.
/// The entity was modified by another request between read and write.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public string EntityName { get; }

    public ConcurrencyConflictException(string entityName)
        : base($"The {entityName} was modified by another request. Please retry your update.")
    {
        EntityName = entityName;
    }

    public ConcurrencyConflictException(string entityName, Exception innerException)
        : base($"The {entityName} was modified by another request. Please retry your update.", innerException)
    {
        EntityName = entityName;
    }
}
