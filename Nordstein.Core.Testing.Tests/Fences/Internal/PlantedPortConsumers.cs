namespace Nordstein.Core.Testing.Tests.Fences.Internal;

// Positive controls for FenceScanExtensions.Mentions: one planted consumer per branch of the walk,
// and one negative control. Never delete them.

/// <summary>An interface that extends the fenced one.</summary>
internal interface IDerivedPort : IPlantedPort
{
}

/// <summary>A class implementing the fenced interface, for the base-type branch.</summary>
internal class PlantedPortBase : IPlantedPort
{
    public void Open()
    {
    }
}

/// <summary>A class deriving from an implementation of the fenced interface.</summary>
internal sealed class PlantedDerivedPortClass : PlantedPortBase
{
}

/// <summary>A custom delegate returning the fenced type.</summary>
internal delegate IPlantedPort PlantedPortSource();

/// <summary>A custom delegate taking the fenced type.</summary>
internal delegate void PlantedPortSink(IPlantedPort port);

/// <summary>Bare constructor parameter.</summary>
internal sealed class PlantedBareConsumer
{
    public PlantedBareConsumer(IPlantedPort port) => Port = port;

    public IPlantedPort Port { get; }
}

/// <summary>Generic argument.</summary>
internal sealed class PlantedWrappedConsumer
{
    public PlantedWrappedConsumer(Lazy<IPlantedPort> port) => Port = port;

    public Lazy<IPlantedPort> Port { get; }
}

/// <summary>Element type of an array.</summary>
internal sealed class PlantedArrayConsumer
{
    public PlantedArrayConsumer(IPlantedPort[] ports) => Ports = ports;

    public IPlantedPort[] Ports { get; }
}

/// <summary>Implemented interface of the parameter type.</summary>
internal sealed class PlantedDerivedInterfaceConsumer
{
    public PlantedDerivedInterfaceConsumer(IDerivedPort port) => Port = port;

    public IDerivedPort Port { get; }
}

/// <summary>Base type of the parameter type.</summary>
internal sealed class PlantedBaseTypeConsumer
{
    public PlantedBaseTypeConsumer(PlantedDerivedPortClass port) => Port = port;

    public PlantedDerivedPortClass Port { get; }
}

/// <summary>Generic-parameter constraint.</summary>
/// <typeparam name="TPort">A port.</typeparam>
internal sealed class PlantedConstrainedConsumer<TPort>
    where TPort : IPlantedPort
{
    public PlantedConstrainedConsumer(TPort port) => Port = port;

    public TPort Port { get; }
}

/// <summary>A delegate whose <c>Invoke</c> returns the fenced type.</summary>
internal sealed class PlantedDelegateReturnConsumer
{
    public PlantedDelegateReturnConsumer(PlantedPortSource source) => Source = source;

    public PlantedPortSource Source { get; }
}

/// <summary>A delegate whose <c>Invoke</c> takes the fenced type.</summary>
internal sealed class PlantedDelegateParameterConsumer
{
    public PlantedDelegateParameterConsumer(PlantedPortSink sink) => Sink = sink;

    public PlantedPortSink Sink { get; }
}

/// <summary>A by-reference parameter of a static method, as an SDK injection point would declare it.</summary>
internal static class PlantedMethodConsumer
{
    public static void Use(ref IPlantedPort port) => port.Open();
}

/// <summary>Negative control: nothing here reaches the fenced type.</summary>
internal sealed class PlantedUnrelatedConsumer
{
    public PlantedUnrelatedConsumer(Lazy<string> name, int[] numbers, Func<Uri> address)
    {
        Name = name;
        Numbers = numbers;
        Address = address;
    }

    public Lazy<string> Name { get; }

    public int[] Numbers { get; }

    public Func<Uri> Address { get; }
}
