using System.Collections.Generic;
using LadybugDB.Extensions;

namespace LadybugDB.Tests.Extensions.Fakes;

internal sealed class FakeResultData : IResultData
{
    public FakeResultData(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        Columns = columns;
        MaterializedRows = rows;
    }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<object?[]> MaterializedRows { get; }

    public IEnumerable<object?[]> EnumerateRows() => MaterializedRows;
}
