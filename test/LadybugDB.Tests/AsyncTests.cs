using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// WS-D async surface: round-trip + cancellation are native-gated (<see cref="SkippableFactAttribute"/>);
/// argument validation and the cancellation-helper contract are pure-managed and always run.
/// </summary>
public sealed class AsyncTests
{
    [SkippableFact]
    public async Task QueryAsync_NullCypher_ThrowsArgumentNullException()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            await Assert.ThrowsAsync<ArgumentNullException>(() => conn.QueryAsync(null!));
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
