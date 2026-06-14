using Microsoft.CodeAnalysis;

namespace LadybugDB.SourceGen;

/// <summary>
/// Placeholder incremental generator so the analyzer project builds and packs as an
/// analyzer asset inside the core LadybugDB package. WS-I replaces the body with the
/// real <c>[LadybugRow]</c> POCO mapper. Emitting nothing keeps the core package
/// behavior unchanged until WS-I lands.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PlaceholderGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Intentionally emits no source. WS-I implements the real generator here.
    }
}
