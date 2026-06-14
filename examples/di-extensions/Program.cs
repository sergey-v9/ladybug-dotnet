using LadybugDB;
using LadybugDB.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

// LadybugDB.Extensions: wire the database into a generic host with DI, a resilience decorator,
// a health check, and result-export helpers. The app owns the Database/Connection lifetime; the
// extension layer wraps the ILadybugExecutor seam.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<Database>(_ => new Database());                 // in-memory for the demo
builder.Services.AddSingleton(sp => new Connection(sp.GetRequiredService<Database>()));
builder.Services.AddSingleton<ILadybugExecutor>(sp =>
    new LadybugConnectionExecutor(sp.GetRequiredService<Connection>()));

builder.Services.AddLadybug(o => { o.DatabasePath = string.Empty; o.MaxThreads = 4; });
builder.Services.AddLadybugResilience();      // timeout + retry + circuit breaker (no Polly)
builder.Services.AddLadybugHealthCheck();     // ASP.NET Core health check

using IHost host = builder.Build();

var executor = host.Services.GetRequiredService<ILadybugExecutor>();   // the resilient decorator

// Seed + query through the resilient executor.
await executor.ExecuteAsync("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))");
await executor.ExecuteAsync("CREATE (:Person {name: 'Alice', age: 30})");
await executor.ExecuteAsync("CREATE (:Person {name: 'Bob', age: 25})");

// Stream typed rows via the IAsyncEnumerable<IRowAccessor> sugar.
Console.WriteLine("--- people ---");
await foreach (IRowAccessor row in executor.StreamAsync(
    "MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    Console.WriteLine($"{row.Get<string>("name")} ({row.Get<long>("age")})");
}

// Run the registered health check.
var health = host.Services.GetRequiredService<HealthCheckService>();
HealthReport report = await health.CheckHealthAsync();
Console.WriteLine($"health: {report.Status}");

// Export helpers operate on a core QueryResult; resolve the Connection for raw results.
var connection = host.Services.GetRequiredService<Connection>();
using QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name");
Console.WriteLine("--- JSON ---");
Console.WriteLine(result.ToJson());
Console.WriteLine("--- CSV ---");
Console.WriteLine(result.ToCsv());
