using System.Net;
using System.Net.Sockets;

namespace Nordstein.Core.Testing.Tests.Fences.Internal;

/// <summary>
/// Positive control for the metadata reference scans: a member reference made only inside an async
/// state machine (a nested, compiler-generated type), a type referenced only by <c>typeof</c>, members
/// of generic instantiations (of another assembly's type, of this assembly's, of a nested one), a
/// multi-dimensional array's constructor and a vararg call site. Never delete it.
/// </summary>
internal sealed class PlantedReferences
{
    public async Task<string> EncodeAsync(string text)
    {
        await Task.Yield();
        return WebUtility.HtmlEncode(text);
    }

    public Type Flags() => typeof(SocketFlags);

    public int Collect()
    {
        var values = new List<int>();
        values.Add(1);
        return values.Count;
    }

    public object Instantiate()
        => new PlantedConstrainedConsumer<PlantedDerivedPortClass>(new PlantedDerivedPortClass()).Port
            + new Logging.Internal.Outer<int>.Inner().ToString();

    public int[,] Grid() => new int[2, 2];

    public int Count() => Varargs(__arglist(1, "two"));

    private static int Varargs(__arglist) => new ArgIterator(__arglist).GetRemainingCount();
}
