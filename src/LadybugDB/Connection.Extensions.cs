using System;

namespace LadybugDB;

/// <summary>
/// Engine-extension helpers. Ladybug has no dedicated C API for extensions: installation and
/// loading run through the normal Cypher query path (<c>INSTALL &lt;name&gt;</c> / <c>LOAD
/// EXTENSION &lt;name&gt;</c>). For a dynamically loaded extension to resolve the engine's symbols
/// on Linux/macOS, <c>liblbug</c> must have been loaded with global symbol visibility — see the
/// resolver in <c>Interop/Native.cs</c> and <c>docs/native-loading-and-extensions.md</c>.
/// </summary>
public sealed partial class Connection
{
    /// <summary>
    /// Installs an engine extension by name (runs <c>INSTALL &lt;name&gt;</c>). Installation contacts
    /// the extension repository and may require network access.
    /// </summary>
    /// <param name="name">The extension name, e.g. <c>"json"</c>, <c>"fts"</c>, <c>"vector"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, whitespace, or not a bare identifier.</exception>
    /// <exception cref="LadybugQueryException">Installation fails (e.g. unknown extension or no network).</exception>
    public void InstallExtension(string name)
    {
        string ext = ValidateExtensionName(name);
        using QueryResult result = Query("INSTALL " + ext);
        _ = result;
    }

    /// <summary>
    /// Loads a previously installed engine extension by name (runs <c>LOAD EXTENSION &lt;name&gt;</c>).
    /// </summary>
    /// <param name="name">The extension name, e.g. <c>"json"</c>, <c>"fts"</c>, <c>"vector"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, whitespace, or not a bare identifier.</exception>
    /// <exception cref="LadybugQueryException">Loading fails (e.g. extension not installed, or — on a
    /// broken loader — the extension cannot resolve the engine's symbols).</exception>
    public void LoadExtension(string name)
    {
        string ext = ValidateExtensionName(name);
        using QueryResult result = Query("LOAD EXTENSION " + ext);
        _ = result;
    }

    private static string ValidateExtensionName(string name)
    {
        name = ThrowHelpers.ThrowIfNull(name, nameof(name));

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Extension name must not be empty or whitespace.", nameof(name));
        }

        // Defensive: an extension name is a bare identifier; reject statement-injection characters so
        // the helper can only ever issue a single INSTALL/LOAD statement.
        foreach (char c in trimmed)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9') || c == '_';
            if (!ok)
            {
                throw new ArgumentException(
                    "Extension name must be a bare identifier (letters, digits, underscore).",
                    nameof(name));
            }
        }

        return trimmed;
    }
}
