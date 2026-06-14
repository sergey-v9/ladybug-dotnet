using System;
using System.Collections.Generic;
using System.Linq;
using LadybugDB;
using LadybugDB.Extensions;
using Xunit;

namespace LadybugDB.Tests.Extensions;

/// <summary>
/// End-to-end conversion through the RowAccessor Select/Scalar path against the real engine. These
/// mirror the source-gen Map&lt;T&gt; coverage: enum over INT64, Guid over UUID, DateTime over DATE,
/// and nullable-widening all flow through RowAccessor.Convert (which now delegates to the shared
/// LadybugRowConvert shim). Before the fix every one of these threw InvalidCastException.
/// </summary>
public sealed class RowAccessorNativeTests
{
    private const string Uuid = "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11";

    private enum Status : long
    {
        Inactive = 0,
        Active = 1,
    }

    private static bool NativeAvailable { get; } = Probe();

    [SkippableFact]
    public void Select_converts_enum_guid_datetime_and_nullable_columns()
    {
        Skip.IfNot(NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);
        using QueryResult result = conn.Query(
            "RETURN 1 AS Status, UUID('" + Uuid + "') AS Id, " +
            "date('2026-06-14') AS Day, 42 AS Count");

        var projected = result.Select(row => new
        {
            Status = row.Get<Status>("Status"),
            Id = row.Get<Guid>("Id"),
            Day = row.Get<DateTime>("Day"),
            Count = row.Get<int?>("Count"),
        }).Single();

        Assert.Equal(Status.Active, projected.Status);
        Assert.Equal(Guid.Parse(Uuid), projected.Id);
        Assert.Equal(new DateTime(2026, 6, 14), projected.Day);
        Assert.Equal(42, projected.Count);
    }

    [SkippableFact]
    public void Scalar_converts_enum_over_int64_column()
    {
        Skip.IfNot(NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);
        using QueryResult result = conn.Query("RETURN 1 AS Status");

        Assert.Equal(Status.Active, result.Scalar<Status>());
    }

    private static bool Probe()
    {
        try
        {
            _ = LadybugVersion.StorageVersion;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (TypeInitializationException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
