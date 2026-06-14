using System;
using System.IO;
using LadybugDB;

namespace LadybugDB.Tests.Parity;

/// <summary>
/// Native-availability gate and shared fixtures for the upstream C-API parity ports. Mirrors
/// <c>LadybugDB.Tests.TestEnvironment</c>; the parity ports cannot reach the engine's gtest harness
/// or its <c>dataset/tinysnb</c> CSVs, so <see cref="CreatePersonGraph"/> rebuilds the slice of the
/// upstream schema each port needs in a fresh temp database.
/// </summary>
internal static class ParityEnvironment
{
    public static readonly bool NativeAvailable = Probe();

    public static string NewTempDbPath()
        => Path.Combine(Path.GetTempPath(), "ladybug-parity-" + Guid.NewGuid().ToString("N"));

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Opens a fresh database and loads a small person graph matching the columns the upstream
    /// tinysnb ports read: fName (STRING), age (INT64), height (FLOAT), isStudent (BOOL),
    /// registerTime (TIMESTAMP), birthdate (DATE), plus a single knows edge. Eight people are
    /// inserted so num-tuples assertions match the upstream "MATCH (a:person)" counts.
    /// Caller owns the returned database and connection.
    /// </summary>
    public static (Database Db, Connection Conn) CreatePersonGraph(out string dbPath)
    {
        dbPath = NewTempDbPath();
        var db = new Database(dbPath);
        var conn = new Connection(db);

        conn.Query(
            "CREATE NODE TABLE person(" +
            "fName STRING, age INT64, height FLOAT, isStudent BOOL, " +
            "registerTime TIMESTAMP, birthdate DATE, PRIMARY KEY(fName))").Dispose();
        conn.Query("CREATE REL TABLE knows(FROM person TO person, since INT64)").Dispose();

        // (name, age, height, isStudent) — three students so the isStudent=true count is 3,
        // matching the upstream "WHERE a.isStudent = true RETURN COUNT(*)" == 3.
        (string Name, long Age, float Height, bool IsStudent)[] people =
        {
            ("Alice", 35, 1.731f, true),
            ("Bob", 30, 1.7f, true),
            ("Carol", 45, 1.6f, false),
            ("Dan", 20, 1.5f, true),
            ("Elizabeth", 20, 1.75f, false),
            ("Farooq", 25, 1.8f, false),
            ("Greg", 40, 1.9f, false),
            ("Hubert", 83, 1.6f, false),
        };
        foreach ((string name, long age, float height, bool isStudent) in people)
        {
            using PreparedStatement insert = conn.Prepare(
                "CREATE (:person {fName: $n, age: $a, height: $h, isStudent: $s, " +
                "registerTime: timestamp('2020-01-15 10:30:00'), birthdate: date('1985-06-01')})");
            insert.Bind("n", name).Bind("a", age).Bind("h", height).Bind("s", isStudent);
            insert.Execute().Dispose();
        }

        conn.Query("MATCH (a:person {fName:'Alice'}), (b:person {fName:'Bob'}) " +
                   "CREATE (a)-[:knows {since: 2011}]->(b)").Dispose();

        return (db, conn);
    }

    private static bool Probe()
    {
        try { _ = LadybugVersion.StorageVersion; return true; }
        catch (DllNotFoundException) { return false; }
        catch (TypeInitializationException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
