using System;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>
/// Port of upstream <c>test/c_api/data_type_test.cpp</c>. The managed binding does not expose a
/// public logical-type constructor; types are observed through <see cref="QueryResult.GetColumnType"/>.
/// </summary>
public sealed class DataTypeParityTests
{
    [SkippableFact] // upstream GetID for INT64 / LIST
    public void Scalar_and_list_logical_type_ids()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        using QueryResult r = conn.Query("RETURN 42 AS scalar, [1, 2, 3] AS list");
        Assert.Equal(DataTypeId.Int64, r.GetColumnType(0).Id);

        LogicalType listType = r.GetColumnType(1);
        Assert.Equal(DataTypeId.List, listType.Id);
        Assert.Equal(DataTypeId.Int64, listType.ChildType!.Id); // upstream getChildType
    }

    [SkippableFact] // upstream GetFixedNumElementsInList: ARRAY honors element count, LIST does not
    public void Array_reports_fixed_size_list_does_not()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        conn.Query("CREATE NODE TABLE A(id INT64, v INT64[3], PRIMARY KEY(id))").Dispose();
        conn.Query("CREATE (:A {id: 1, v: [10, 20, 30]})").Dispose();

        using QueryResult arr = conn.Query("MATCH (a:A) RETURN a.v");
        LogicalType arrType = arr.GetColumnType(0);
        Assert.Equal(DataTypeId.Array, arrType.Id);
        Assert.Equal(3UL, arrType.FixedArraySize);

        using QueryResult lst = conn.Query("RETURN [1, 2, 3] AS v");
        Assert.Null(lst.GetColumnType(0).FixedArraySize); // LIST has no fixed size
    }

    [SkippableFact] // upstream Equals/Clone observable as ToString stability
    public void Logical_type_to_string_is_descriptive()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        using QueryResult r = conn.Query("RETURN [1, 2, 3] AS list");
        string s = r.GetColumnType(0).ToString();
        Assert.Contains("LIST", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INT64", s, StringComparison.OrdinalIgnoreCase);
    }
}
