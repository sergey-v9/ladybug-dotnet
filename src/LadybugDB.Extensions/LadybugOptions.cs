namespace LadybugDB.Extensions;

/// <summary>Configuration for the Ladybug database registered through dependency injection.</summary>
public sealed class LadybugOptions
{
    /// <summary>Filesystem path to the database directory. Empty string opens an in-memory database.</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>Opens the database read-only when <see langword="true"/>.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Maximum threads for query execution; 0 leaves the engine default in place.</summary>
    public ulong MaxThreads { get; set; }

    /// <summary>Buffer-pool size in bytes; 0 leaves the engine default in place.</summary>
    public ulong BufferPoolSize { get; set; }
}
