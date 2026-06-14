// Polyfill so C# records / init-only setters compile on netstandard2.0 (the type ships in-box
// only on net5.0+). Internal so it does not leak from the analyzer assembly.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
