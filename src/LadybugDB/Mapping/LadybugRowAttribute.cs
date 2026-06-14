using System;

namespace LadybugDB;

/// <summary>
/// Marks a record, class, or struct as a target of the LadybugDB POCO source generator. The
/// generator emits reflection-free <see cref="O:LadybugDB.LadybugRowMappers.Map"/> /
/// <c>MapAsync</c> extension methods on <see cref="QueryResult"/> that materialize result rows
/// into instances of the annotated type, mapping result columns to constructor parameters or
/// settable properties by name (case-insensitive). No runtime reflection is used, so mapping is
/// AOT-safe.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class LadybugRowAttribute : Attribute
{
}
