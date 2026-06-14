using System.Collections.Generic;
using System.Threading.Tasks;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class ExecutorSeamTests
{
    [Fact]
    public async Task Executor_returns_materialized_columns_and_rows()
    {
        var result = new LadybugExecutionResult(
            new[] { "name", "age" },
            new List<object?[]> { new object?[] { "Alice", 30L } });
        var executor = FakeLadybugExecutor.Returning(result);

        LadybugExecutionResult actual = await executor.ExecuteAsync("RETURN 1");

        Assert.Equal("fake", executor.Name);
        Assert.Equal(new[] { "name", "age" }, actual.Columns);
        Assert.Single(actual.Rows);
        Assert.Equal("Alice", actual.Rows[0][0]);
        Assert.Equal(30L, actual.Rows[0][1]);
    }

    [Fact]
    public void Empty_result_has_no_columns_or_rows()
    {
        Assert.Empty(LadybugExecutionResult.Empty.Columns);
        Assert.Empty(LadybugExecutionResult.Empty.Rows);
    }
}
