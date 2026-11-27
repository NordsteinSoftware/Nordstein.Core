namespace Nordstein.Core.Testing.Tests.Fences.Internal;

// Positive control for FenceScanExtensions.StaticMembers: the static members the scan must catch,
// and the ones it must let through. Never delete it.

/// <summary>Nine violations among the allowed forms.</summary>
internal sealed class PlantedStaticMembers
{
    public const int Limit = 3;

    public static readonly string Prefix = "planted";

    public static int Counter = 1;

#pragma warning disable IDE0044 // WHY: a planted mutable static field; the scan under test must report it.
    private static int hiddenCounter = 2;
#pragma warning restore IDE0044

    public static event EventHandler? Changed;

    public static string Shared { get; } = "shared";

    public static int Settable { get; set; }

    private static int HiddenSettable { get; set; }

    public static int WriteOnly
    {
        set => hiddenCounter = value;
    }

    public static string Shout(string text) => text.ToUpperInvariant();

    internal static int Twice(int value) => value * 2;

    public static PlantedStaticMembers operator +(PlantedStaticMembers left, PlantedStaticMembers right)
        => left.Add(right);

    private static int Hidden() => Limit + hiddenCounter + HiddenSettable;

    private PlantedStaticMembers Add(PlantedStaticMembers other)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return Hidden() > 0 ? other : this;
    }
}

/// <summary>An extension method: allowed.</summary>
internal static class PlantedStaticExtensions
{
    public static string Quiet(this string text) => text.ToLowerInvariant();
}
