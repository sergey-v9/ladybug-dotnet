using LadybugDB;

// Async: Task-returning queries, IAsyncEnumerable streaming, and a CancellationToken wired to the
// engine interrupt. The synchronous engine call is offloaded while honoring the connection's gate.

using Database database = new();
using Connection connection = new(database);

await connection.QueryAsync("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))");
for (int i = 0; i < 5; i++)
{
    await connection.QueryAsync($"CREATE (:Person {{name: 'P{i}', age: {20 + i}}})");
}

// Buffered async query.
using (QueryResult result = await connection.QueryAsync("MATCH (p:Person) RETURN p.name, p.age ORDER BY p.name"))
{
    foreach (object?[] row in result.Rows())
    {
        Console.WriteLine($"{row[0]}: {row[1]}");
    }
}

// Streaming async: rows arrive one at a time as IAsyncEnumerable<FlatTuple>.
Console.WriteLine("--- streamed ---");
await foreach (FlatTuple tuple in connection.StreamAsync("MATCH (p:Person) RETURN p.name ORDER BY p.name"))
{
    using (tuple)
    using (Value value = tuple.GetValue(0))
    {
        Console.WriteLine(value.GetValue());
    }
}

// Cancellation: the token is registered with the engine interrupt, so a long query is aborted.
using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
try
{
    await connection.QueryAsync(
        "UNWIND range(1, 100000000) AS a UNWIND range(1, 1000) AS b RETURN count(*)",
        cts.Token);
    Console.WriteLine("query completed before the timeout");
}
catch (OperationCanceledException)
{
    Console.WriteLine("query cancelled via CancellationToken");
}
