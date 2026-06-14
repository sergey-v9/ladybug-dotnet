namespace LadybugDB;

/// <summary>The name and logical type of a single result column.</summary>
public sealed record ColumnSchema(string Name, LogicalType Type);
