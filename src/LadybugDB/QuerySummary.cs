namespace LadybugDB;

/// <summary>
/// Timing breakdown for an executed query: the planner's compilation time and the executor's run
/// time, both in milliseconds. Sourced from the engine's <c>lbug_query_summary</c>.
/// </summary>
public readonly record struct QuerySummary(double CompilingTimeMs, double ExecutionTimeMs);
