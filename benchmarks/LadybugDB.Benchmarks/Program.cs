// Skeleton benchmark host. WS-J adds the real BenchmarkDotNet suites and the --ci-gate guard.
// Returns 0 so the project builds and a no-arg run exits cleanly.
namespace LadybugDB.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        System.Console.WriteLine("LadybugDB.Benchmarks skeleton. WS-J adds the BenchmarkDotNet suites.");
        return 0;
    }
}
