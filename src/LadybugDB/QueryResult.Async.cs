namespace LadybugDB;

/// <summary>
/// Asynchronous seam for <see cref="QueryResult"/>. WS-D owns this partial file so that streaming
/// (which lives on <see cref="Connection"/> because it holds the <c>_gate</c>) and a future
/// <c>MapAsync&lt;T&gt;</c> (WS-I) can be added without editing <c>QueryResult.cs</c> (WS-B).
/// </summary>
public sealed partial class QueryResult
{
}
