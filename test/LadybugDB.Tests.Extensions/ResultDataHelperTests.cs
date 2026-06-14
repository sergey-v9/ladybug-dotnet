using System.Collections.Generic;
using System.Data;
using System.Linq;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class ResultDataHelperTests
{
    private static FakeResultData Sample() => new(
        new[] { "name", "age" },
        new List<object?[]>
        {
            new object?[] { "Alice", 30L },
            new object?[] { "Bob", null },
        });

    [Fact]
    public void ToDictionaries_keys_by_column_name()
    {
        var dicts = ResultHelpers.ToDictionaries(Sample()).ToList();
        Assert.Equal(2, dicts.Count);
        Assert.Equal("Alice", dicts[0]["name"]);
        Assert.Equal(30L, dicts[0]["age"]);
        Assert.Null(dicts[1]["age"]);
    }

    [Fact]
    public void Scalar_reads_first_row_named_column_case_insensitively()
    {
        Assert.Equal("Alice", ResultHelpers.Scalar<string>(Sample(), 0));
    }

    [Fact]
    public void ToJsonArray_emits_array_of_objects()
    {
        string json = ResultHelpers.ToJsonArray(Sample());
        Assert.Contains("\"name\":\"Alice\"", json);
        Assert.Contains("\"age\":30", json);
    }

    [Fact]
    public void ToCsv_quotes_fields_with_separator_and_handles_nulls()
    {
        var data = new FakeResultData(
            new[] { "a", "b" },
            new List<object?[]> { new object?[] { "x,y", null } });
        string csv = ResultHelpers.ToCsv(data, ',');
        string[] lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal("a,b", lines[0]);
        Assert.Equal("\"x,y\",", lines[1]);
    }

    [Fact]
    public void ToDataTable_maps_nulls_to_dbnull()
    {
        DataTable table = ResultHelpers.ToDataTable(Sample());
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(System.DBNull.Value, table.Rows[1]["age"]);
    }
}
