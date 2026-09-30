namespace NittyGriddy.Tests;

/// <summary>
/// Tests that create WPF objects each run them on a UI thread of their own. WPF's loading of compiled XAML is
/// not safe when several such threads do it at once (the application itself has a single UI thread), so these
/// test classes are kept from running in parallel with each other and with everything else.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class WpfCollection
{
    public const string Name = "WPF";
}
