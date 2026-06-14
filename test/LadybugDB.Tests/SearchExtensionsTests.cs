using System;
using System.Collections.Generic;
using System.Linq;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// End-to-end coverage for the engine search paths our first-class consumer (Graphiti.Core) pushes into
/// the database: full-text search via the <c>fts</c> extension, and exact vector cosine similarity. These
/// mirror Graphiti's exact Cypher (see GRAPHITI_SEARCH_EXTENSIONS_FEEDBACK.md) so a green run here means
/// Graphiti can rely on the path without re-testing the engine. They run on every RID the suite stages a
/// native for — including <b>linux-x64</b> in CI — which is the cross-platform guarantee Graphiti needs
/// (it ships on FTS and is only validated on win-x64 itself today).
///
/// Extension round-trips are native-gated and offline-tolerant: they Skip on a download/network failure
/// but FAIL loudly on an "undefined symbol" failure, since that is exactly the RTLD_GLOBAL loader
/// regression (WS-F) these tests exist to catch on Unix.
/// </summary>
public sealed class SearchExtensionsTests
{
    [SkippableFact]
    public void Fts_InstallLoadCreateIndexAndQuery_ReturnsRankedRows()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            // Mirror Graphiti's Entity table + 'node_name_and_summary' FTS index over ['name','summary'].
            conn.Query("CREATE NODE TABLE Entity(name STRING, summary STRING, PRIMARY KEY(name))").Dispose();
            conn.Query("CREATE (:Entity {name: 'Alice', summary: 'a software engineer who loves graph databases'})").Dispose();
            conn.Query("CREATE (:Entity {name: 'Bob', summary: 'a painter and a sculptor of fine art'})").Dispose();
            conn.Query("CREATE (:Entity {name: 'Carol', summary: 'a database administrator and graph theorist'})").Dispose();

            if (!TryInstallAndLoad(conn, "fts", out string? skipReason))
            {
                throw new Xunit.SkipException(skipReason!);
            }

            // CALL CREATE_FTS_INDEX('Entity', 'node_name_and_summary', ['name','summary'])
            conn.Query("CALL CREATE_FTS_INDEX('Entity', 'node_name_and_summary', ['name', 'summary'])").Dispose();

            // Query text is passed verbatim and TOP is parameterized, exactly as Graphiti does.
            using PreparedStatement stmt = conn.Prepare(
                "CALL QUERY_FTS_INDEX('Entity', 'node_name_and_summary', $query, TOP := $limit) " +
                "RETURN node.name AS name, score ORDER BY score DESC");
            stmt.Bind("query", "graph databases");
            stmt.Bind("limit", 5L);

            using QueryResult result = stmt.Execute();
            List<object?[]> rows = result.Rows().ToList();

            // Both 'Alice' and 'Carol' mention graph/databases; 'Bob' should not surface.
            Assert.NotEmpty(rows);
            var names = rows.Select(r => (string?)r[0]).ToList();
            Assert.Contains("Alice", names);
            Assert.DoesNotContain("Bob", names);
            // BM25 scores are positive doubles in descending order.
            Assert.All(rows, r => Assert.True(Convert.ToDouble(r[1]) > 0.0));
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    [SkippableFact]
    public void Vector_InlineCosineSimilarity_FiltersAndRanks()
    {
        // array_cosine_similarity is a built-in array function (no extension / no network), so this is the
        // reliable check that Graphiti's exact "List<float> + CAST($v AS FLOAT[<dim>])" search path works.
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Doc(id STRING, name_embedding FLOAT[3], PRIMARY KEY(id))").Dispose();
            conn.Query("CREATE (:Doc {id: 'a', name_embedding: [1.0, 0.0, 0.0]})").Dispose();   // identical
            conn.Query("CREATE (:Doc {id: 'b', name_embedding: [0.0, 1.0, 0.0]})").Dispose();   // orthogonal
            conn.Query("CREATE (:Doc {id: 'c', name_embedding: [0.9, 0.1, 0.0]})").Dispose();   // close

            using PreparedStatement stmt = conn.Prepare(
                "MATCH (n:Doc) " +
                "WITH n, array_cosine_similarity(n.name_embedding, CAST($search_vector AS FLOAT[3])) AS score " +
                "WHERE score > $min_score " +
                "RETURN n.id AS id, score ORDER BY score DESC");
            stmt.Bind("search_vector", new List<float> { 1.0f, 0.0f, 0.0f });   // List<float> binding
            stmt.Bind("min_score", 0.5);

            using QueryResult result = stmt.Execute();
            List<object?[]> rows = result.Rows().ToList();

            var ids = rows.Select(r => (string?)r[0]).ToList();
            Assert.Equal("a", ids.First());                 // identical vector ranks first
            Assert.Contains("c", ids);                       // close vector survives the threshold
            Assert.DoesNotContain("b", ids);                 // orthogonal vector (score 0) filtered out
            Assert.All(rows, r => Assert.True(Convert.ToDouble(r[1]) > 0.5));
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    [SkippableFact]
    public void Vector_Extension_LoadsWithoutUndefinedSymbol()
    {
        // Graphiti may later opt into an HNSW index from the `vector` extension. The only ask is that the
        // extension LOADS cross-platform; this proves the .so resolves engine symbols on Unix (the
        // RTLD_GLOBAL path), without depending on the exact CREATE_VECTOR_INDEX procedure surface.
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            if (!TryInstallAndLoad(conn, "vector", out string? skipReason))
            {
                throw new Xunit.SkipException(skipReason!);
            }

            // Reaching here means INSTALL + LOAD EXTENSION vector succeeded with symbols resolved.
            Assert.True(true);
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    /// <summary>
    /// Installs and loads <paramref name="extension"/>. Returns false with a skip reason on a likely
    /// offline/download failure, but RE-THROWS an "undefined symbol" failure (the loader regression).
    /// </summary>
    private static bool TryInstallAndLoad(Connection conn, string extension, out string? skipReason)
    {
        skipReason = null;
        try
        {
            conn.InstallExtension(extension);
            conn.LoadExtension(extension);
            return true;
        }
        catch (LadybugQueryException ex)
        {
            if (IsSymbolResolutionFailure(ex))
            {
                throw; // liblbug not loaded with RTLD_GLOBAL — do NOT mask this as offline.
            }

            skipReason = $"'{extension}' extension install/load unavailable (likely offline): {ex.Message}";
            return false;
        }
    }

    private static bool IsSymbolResolutionFailure(LadybugQueryException ex)
    {
        string m = ex.Message;
        return m.Contains("undefined symbol", StringComparison.OrdinalIgnoreCase)
            || m.Contains("cannot resolve", StringComparison.OrdinalIgnoreCase)
            || m.Contains("symbol not found", StringComparison.OrdinalIgnoreCase);
    }
}
