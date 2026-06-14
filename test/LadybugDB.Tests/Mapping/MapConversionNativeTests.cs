using System;
using System.Collections.Generic;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Mapping;

/// <summary>Status enum backed by INT64 (the engine's integral surface for an INT64 column).</summary>
public enum RowStatus : long
{
    Inactive = 0,
    Active = 1,
    Archived = 2,
}

/// <summary>
/// A row whose members exercise every conversion the headline Map&lt;T&gt; crash hit: an enum over an
/// INT64 column, nullable-widening (INT64 -&gt; int?, DOUBLE -&gt; float?, DECIMAL -&gt; decimal?), a Guid
/// over a UUID column, and a DateTime over a DATE column.
/// </summary>
[LadybugRow]
public sealed record TypedRow(
    RowStatus Status,
    int? Count,
    float? Ratio,
    decimal? Amount,
    Guid Id,
    DateTime Day);

/// <summary>
/// End-to-end conversion through the source-generated Map&lt;T&gt; path against the real engine. Before
/// the FIX-MAP fix every one of these columns threw InvalidCastException inside LadybugRowConvert.
/// </summary>
public sealed class MapConversionNativeTests
{
    private const string Uuid = "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11";

    [SkippableFact]
    public void Map_converts_enum_nullable_guid_and_datetime_columns()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);

        using QueryResult result = conn.Query(
            "RETURN 1 AS Status, 7 AS Count, CAST(1.5 AS DOUBLE) AS Ratio, " +
            "CAST(3.14 AS DECIMAL(10, 2)) AS Amount, " +
            "UUID('" + Uuid + "') AS Id, date('2026-06-14') AS Day");

        IReadOnlyList<TypedRow> rows = result.Map<TypedRow>();

        TypedRow row = Assert.Single(rows);
        Assert.Equal(RowStatus.Active, row.Status);
        Assert.Equal(7, row.Count);
        Assert.Equal(1.5f, row.Ratio);
        Assert.Equal(3.14m, row.Amount);
        Assert.Equal(Guid.Parse(Uuid), row.Id);
        Assert.Equal(new DateTime(2026, 6, 14), row.Day);
    }

    [SkippableFact]
    public void Map_maps_null_columns_to_member_defaults()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);

        // Nullable members over NULL columns must materialize as null (not throw); the non-nullable
        // enum/Guid/DateTime over NULL fall to default.
        using QueryResult result = conn.Query(
            "RETURN CAST(NULL AS INT64) AS Status, CAST(NULL AS INT64) AS Count, " +
            "CAST(NULL AS DOUBLE) AS Ratio, CAST(NULL AS DECIMAL(10, 2)) AS Amount, " +
            "CAST(NULL AS UUID) AS Id, CAST(NULL AS DATE) AS Day");

        IReadOnlyList<TypedRow> rows = result.Map<TypedRow>();

        TypedRow row = Assert.Single(rows);
        Assert.Equal(default(RowStatus), row.Status);
        Assert.Null(row.Count);
        Assert.Null(row.Ratio);
        Assert.Null(row.Amount);
        Assert.Equal(default(Guid), row.Id);
        Assert.Equal(default(DateTime), row.Day);
    }
}
