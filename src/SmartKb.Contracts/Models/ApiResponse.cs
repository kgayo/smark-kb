namespace SmartKb.Contracts.Models;

public sealed record ApiResponse<T>(
    T? Data,
    string? Error,
    string CorrelationId)
{
    public bool IsSuccess => Error is null;

    public static ApiResponse<T> Success(T data, string correlationId) =>
        new(data, null, correlationId);

    public static ApiResponse<T> Failure(string error, string correlationId) =>
        new(default, error, correlationId);
}
