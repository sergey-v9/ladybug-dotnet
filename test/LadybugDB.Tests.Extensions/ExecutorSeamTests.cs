using LadybugDB.Extensions;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class ExecutorSeamTests
{
    [Fact]
    public void Empty_result_has_no_columns_or_rows()
    {
        Assert.Empty(LadybugExecutionResult.Empty.Columns);
        Assert.Empty(LadybugExecutionResult.Empty.Rows);
    }
}
