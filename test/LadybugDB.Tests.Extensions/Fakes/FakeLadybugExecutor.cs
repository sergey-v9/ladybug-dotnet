using System;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB.Extensions;

namespace LadybugDB.Tests.Extensions.Fakes;

/// <summary>In-memory executor used by the extension tests; needs no native engine.</summary>
internal sealed class FakeLadybugExecutor : ILadybugExecutor
{
    private readonly Func<string, CancellationToken, Task<LadybugExecutionResult>> _onExecute;

    public int ExecuteCount;

    public FakeLadybugExecutor(Func<string, CancellationToken, Task<LadybugExecutionResult>> onExecute)
    {
        _onExecute = onExecute;
        Name = "fake";
    }

    public static FakeLadybugExecutor Returning(LadybugExecutionResult result)
        => new((_, _) => Task.FromResult(result));

    public string Name { get; set; }

    public Task<LadybugExecutionResult> ExecuteAsync(string cypher, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecuteCount);
        return _onExecute(cypher, cancellationToken);
    }
}
