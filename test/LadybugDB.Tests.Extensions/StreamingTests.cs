using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class StreamingTests
{
    [Fact]
    public async Task StreamAsync_yields_one_row_accessor_per_row()
    {
        var executor = FakeLadybugExecutor.Returning(new LadybugExecutionResult(
            new[] { "name" },
            new List<object?[]>
            {
                new object?[] { "Alice" },
                new object?[] { "Bob" },
            }));

        var names = new List<string>();
        await foreach (IRowAccessor row in executor.StreamAsync("MATCH (p) RETURN p.name"))
        {
            names.Add(row.Get<string>("name"));
        }

        Assert.Equal(new[] { "Alice", "Bob" }, names);
    }

    [Fact]
    public async Task StreamAsync_with_selector_projects_each_row()
    {
        var executor = FakeLadybugExecutor.Returning(new LadybugExecutionResult(
            new[] { "n" },
            new List<object?[]> { new object?[] { 1L }, new object?[] { 2L } }));

        var sum = 0L;
        await foreach (long n in executor.StreamAsync("RETURN 1", r => r.Get<long>("n")))
        {
            sum += n;
        }

        Assert.Equal(3L, sum);
    }

    [Fact]
    public async Task StreamAsync_honors_cancellation_before_enumeration()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var executor = FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in executor.StreamAsync("RETURN 1", cts.Token))
            {
            }
        });
    }
}
