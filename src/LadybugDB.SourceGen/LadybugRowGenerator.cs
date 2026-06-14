using Microsoft.CodeAnalysis;

namespace LadybugDB.SourceGen;

/// <summary>
/// Incremental source generator that emits reflection-free <c>Map&lt;T&gt;()</c> /
/// <c>MapAsync&lt;T&gt;()</c> materializers for every <c>[LadybugRow]</c> type.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class LadybugRowGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filled in by later tasks.
    }
}
