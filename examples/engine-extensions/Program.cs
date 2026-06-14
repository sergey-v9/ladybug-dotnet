using LadybugDB;

// Engine extensions: install and load an official extension, then use it. On Linux/macOS the binding
// loads the native engine with global symbol visibility (dlopen RTLD_GLOBAL) so the dynamically loaded
// extension shared object can resolve the engine's symbols.

using Database database = new();
using Connection connection = new(database);

try
{
    // INSTALL downloads the extension on first use (requires network access to the extension repo).
    connection.InstallExtension("json");
    connection.LoadExtension("json");
    Console.WriteLine("json extension installed and loaded");

    // Use a function the json extension provides.
    using QueryResult result = connection.Query("RETURN to_json([1, 2, 3]) AS j");
    foreach (object?[] row in result.Rows())
    {
        Console.WriteLine($"to_json([1,2,3]) = {row[0]}");
    }
}
catch (LadybugQueryException ex)
{
    // The most common failure is no network access to fetch the extension on a clean machine.
    Console.WriteLine($"Could not install/load the extension (offline?): {ex.Message}");
}
