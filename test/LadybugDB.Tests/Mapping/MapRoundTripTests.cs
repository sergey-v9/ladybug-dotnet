using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests.Mapping;

[LadybugRow]
public sealed record PersonRow(string Name, long Age);

public sealed class MapRoundTripTests
{
    [SkippableFact]
    public void Map_materializes_rows_into_poco()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Person(Name STRING, Age INT64, PRIMARY KEY(Name))").Dispose();
            conn.Query("CREATE (:Person {Name: 'Alice', Age: 30})").Dispose();
            conn.Query("CREATE (:Person {Name: 'Bob', Age: 42})").Dispose();

            using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.Name AS Name, p.Age AS Age ORDER BY p.Age");

            IReadOnlyList<PersonRow> people = result.Map<PersonRow>();

            Assert.Equal(2, people.Count);
            Assert.Equal(new PersonRow("Alice", 30), people[0]);
            Assert.Equal(new PersonRow("Bob", 42), people[1]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public async Task MapAsync_streams_rows_into_poco()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Person(Name STRING, Age INT64, PRIMARY KEY(Name))").Dispose();
            conn.Query("CREATE (:Person {Name: 'Alice', Age: 30})").Dispose();
            conn.Query("CREATE (:Person {Name: 'Bob', Age: 42})").Dispose();

            using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.Name AS Name, p.Age AS Age ORDER BY p.Age");

            var people = new List<PersonRow>();
            await foreach (PersonRow p in result.MapAsync<PersonRow>())
            {
                people.Add(p);
            }

            Assert.Equal(2, people.Count);
            Assert.Equal(new PersonRow("Alice", 30), people[0]);
            Assert.Equal(new PersonRow("Bob", 42), people[1]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
