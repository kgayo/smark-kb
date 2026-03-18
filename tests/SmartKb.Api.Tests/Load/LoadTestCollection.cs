namespace SmartKb.Api.Tests.Load;

/// <summary>
/// Serializes load test classes so they don't run concurrently against the same
/// in-memory SQLite database (which only supports a single writer).
/// </summary>
[CollectionDefinition("LoadTests", DisableParallelization = true)]
public class LoadTestCollection;
