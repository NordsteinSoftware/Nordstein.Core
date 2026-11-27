namespace Nordstein.Core.Testing.Tests.Fences;

// Positive controls for FenceScanExtensions.InternalTypesOutsideInternalNamespaces: internal types
// of every kind outside an Internal namespace (caught), among the forms the rule allows. Never
// delete them.

/// <summary>Caught: an internal class.</summary>
internal sealed class PlantedMisplacedClass
{
    // A lambda and an async method: their compiler-generated types are nested, never caught.
    public Func<int, int> Twice { get; } = value => value * 2;

    public async Task<int> LaterAsync()
    {
        await Task.Yield();
        return Twice(2);
    }
}

/// <summary>Caught: an internal interface.</summary>
internal interface IPlantedMisplaced
{
}

/// <summary>Caught: an internal enum.</summary>
internal enum PlantedMisplacedKind
{
    One,
}

/// <summary>Caught: an internal delegate.</summary>
internal delegate void PlantedMisplacedCallback();

/// <summary>Caught: an internal record.</summary>
internal sealed record PlantedMisplacedRecord(string Name);

/// <summary>Allowed: a public type, with nested types that live in its file.</summary>
public sealed class PlantedPublicHost
{
    internal string Describe() => new NestedHelper().ToString() + new PrivateNested() + new PlantedFileLocal();

    internal sealed class NestedHelper
    {
    }

    private sealed class PrivateNested
    {
    }
}

/// <summary>Allowed: a file-local type is private to its file.</summary>
file sealed class PlantedFileLocal
{
}
