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
            // The corpus is shaped so relevance is UNAMBIGUOUS, not score-margin-dependent: 'Alice' repeats
            // the query terms (clearly most relevant), 'Carol' is a partial match (graph + database), 'Bob'
            // is irrelevant. We then assert relevance-ordering PROPERTIES, never exact BM25 scores, so this
            // survives the 0.18.0 FTS/BM25 bookkeeping changes Graphiti flagged (feedback wish #6).
            conn.Query("CREATE NODE TABLE Entity(name STRING, summary STRING, PRIMARY KEY(name))").Dispose();
            conn.Query("CREATE (:Entity {name: 'Alice', summary: 'graph databases graph databases the definitive guide to graph databases'})").Dispose();
            conn.Query("CREATE (:Entity {name: 'Carol', summary: 'a database administrator and graph theorist'})").Dispose();
            conn.Query("CREATE (:Entity {name: 'Bob', summary: 'a painter and a sculptor of fine art'})").Dispose();

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

            // --- Robust, score-tweak-resilient ranking cross-check (wish #6) ---
            // We assert RELEVANCE properties, never exact scores, so a BM25 k/b shift at 0.18.0 cannot
            // break this while a genuine ranking regression still would.
            Assert.NotEmpty(rows);
            var names = rows.Select(r => (string?)r[0]).ToList();
            var scores = rows.Select(r => Convert.ToDouble(r[1])).ToList();

            // (1) The clearly-most-relevant entity ranks FIRST.
            Assert.Equal("Alice", names[0]);
            // (2) The partial match is present (it does match), the irrelevant entity is absent.
            Assert.Contains("Carol", names);
            Assert.DoesNotContain("Bob", names);
            // (3) Scores are positive and in DESCENDING order (the engine's stated QUERY_FTS_INDEX contract).
            Assert.All(scores, s => Assert.True(s > 0.0, "every matched row should have a positive BM25 score"));
            for (int i = 1; i < scores.Count; i++)
            {
                Assert.True(scores[i - 1] >= scores[i],
                    $"scores must be descending: row {i - 1} ({scores[i - 1]}) >= row {i} ({scores[i]})");
            }
        }
        finally { TestEnvironment.TryDelete(dbPath); }
    }

    /// <summary>
    /// Pins the <c>DROP_FTS_INDEX</c> contract Graphiti will depend on when it drops its
    /// "Index … already exists" message-catch idempotency workaround (feedback wish #2).
    ///
    /// The <c>CALL DROP_FTS_INDEX</c> DDL is NEW in engine 0.18.0/main, so this test is
    /// VERSION-TOLERANT: it Skips gracefully when the loaded native lacks the procedure (older staged
    /// natives), and VALIDATES the contract for real when present — which is the case in the dev-feed CI
    /// Test gate that runs against the 0.18.0-dev native.
    ///
    /// We use the FTS-specific <c>DROP_FTS_INDEX</c> procedure, NOT generic <c>DROP INDEX</c>: only the
    /// former is guaranteed to clean the FTS auxiliary docs/terms/appears-in tables, and on some natives
    /// generic <c>DROP INDEX</c> is not even parseable.
    /// </summary>
    [SkippableFact]
    public void DropFtsIndex_RoundTrip_PinsCleanupAndMissingIndexThrows()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Entity(name STRING, summary STRING, PRIMARY KEY(name))").Dispose();
            conn.Query("CREATE (:Entity {name: 'Alice', summary: 'a guide to graph databases'})").Dispose();
            conn.Query("CREATE (:Entity {name: 'Carol', summary: 'a database administrator and graph theorist'})").Dispose();

            if (!TryInstallAndLoad(conn, "fts", out string? ftsSkip))
            {
                throw new Xunit.SkipException(ftsSkip!);
            }

            // Create the index, then exercise the first DROP_FTS_INDEX. This first drop call is also our
            // version probe: an older native without the DDL raises a Catalog/Binder "function does not
            // exist" / unknown-procedure error here, which we translate into a clean Skip. Because the
            // index DOES exist, a native that HAS the procedure cannot fail this call for any other reason,
            // so we never mask a real regression.
            conn.Query("CALL CREATE_FTS_INDEX('Entity', 'idx', ['name', 'summary'])").Dispose();

            try
            {
                conn.Query("CALL DROP_FTS_INDEX('Entity', 'idx')").Dispose();
            }
            catch (LadybugQueryException ex) when (IsProcedureNotPresent(ex))
            {
                throw new Xunit.SkipException(
                    "DROP_FTS_INDEX DDL is not present in the loaded native (introduced in engine 0.18.0); " +
                    $"skipping the round-trip until the dev-feed native is adopted. Engine said: {ex.Message}");
            }

            // (a) CLEAN DROP-THEN-CREATE IDEMPOTENCY. Recreating the same index must SUCCEED — this is the
            // observable proof the auxiliary docs/terms/appears-in tables were cleaned by DROP_FTS_INDEX. A
            // stale aux table would make this CREATE fail.
            conn.Query("CALL CREATE_FTS_INDEX('Entity', 'idx', ['name', 'summary'])").Dispose();

            // ...and a subsequent QUERY_FTS_INDEX returns rows over the recreated index (the index is live,
            // not just metadata that re-created without a backing structure).
            using (PreparedStatement query = conn.Prepare(
                "CALL QUERY_FTS_INDEX('Entity', 'idx', $query, TOP := $limit) " +
                "RETURN node.name AS name, score ORDER BY score DESC"))
            {
                query.Bind("query", "graph databases");
                query.Bind("limit", 5L);
                using QueryResult queryResult = query.Execute();
                List<object?[]> rows = queryResult.Rows().ToList();
                Assert.NotEmpty(rows);
                Assert.All(rows, r => Assert.True(Convert.ToDouble(r[1]) > 0.0));
            }

            // (b) DROP ON A MISSING INDEX THROWS. We have already proven the DDL exists (the drop above
            // succeeded), so this throw is the genuine missing-index error — not the "procedure missing"
            // signal. This pins that a naive drop-then-create is NOT idempotent on its own, so Graphiti
            // still needs a guard around the drop.
            Assert.Throws<LadybugQueryException>(() =>
                conn.Query("CALL DROP_FTS_INDEX('Entity', 'no_such_index')").Dispose());
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

    /// <summary>
    /// True when <paramref name="ex"/> indicates the called procedure (e.g. <c>DROP_FTS_INDEX</c>) does
    /// not exist in the loaded native — i.e. an older engine that predates the DDL. The engine reports an
    /// unknown procedure as a Catalog/Binder "function … does not exist" error (observed:
    /// <c>"Catalog exception: function DROP_FTS_INDEX does not exist."</c>); we also tolerate the
    /// "unknown function/procedure" phrasings so the skip stays robust across engine versions.
    /// </summary>
    private static bool IsProcedureNotPresent(LadybugQueryException ex)
    {
        string m = ex.Message;
        return m.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Catalog exception", StringComparison.OrdinalIgnoreCase)
            || m.Contains("unknown function", StringComparison.OrdinalIgnoreCase)
            || m.Contains("unknown procedure", StringComparison.OrdinalIgnoreCase)
            || m.Contains("nonexistent function", StringComparison.OrdinalIgnoreCase);
    }
}
