using System.Reflection;
using AwesomeAssertions;
using Nordstein.Core.Testing.Fences;
using Nordstein.Core.Testing.Tests.Fences.Internal;
using Nordstein.Core.Testing.Tests.Logging.Internal;

namespace Nordstein.Core.Testing.Tests.Fences;

/// <summary>
/// The fence kit's scans against the planted fixtures of this assembly: every branch of each scan
/// finds its planted violation, and the negative controls stay clean.
/// </summary>
[TestClass]
public sealed class FenceScanExtensionsTests
{
    private const string Planted = "Nordstein.Core.Testing.Tests.Fences.Internal.";

    private static Assembly ThisAssembly => typeof(FenceScanExtensionsTests).Assembly;

    [TestMethod]
    public void ParametersMentioning_FindsThePlantedConsumerOnEveryBranchOfTheWalk()
        => ThisAssembly.ParametersMentioning(type => type == typeof(IPlantedPort))
            .Where(hit => hit.StartsWith(Planted, StringComparison.Ordinal))
            .Should().Equal(
                Planted + "PlantedArrayConsumer: IPlantedPort[]",
                Planted + "PlantedBareConsumer: IPlantedPort",
                Planted + "PlantedBaseTypeConsumer: PlantedDerivedPortClass",
                Planted + "PlantedConstrainedConsumer`1: TPort",
                Planted + "PlantedDelegateParameterConsumer: PlantedPortSink",
                Planted + "PlantedDelegateReturnConsumer: PlantedPortSource",
                Planted + "PlantedDerivedInterfaceConsumer: IDerivedPort",
                Planted + "PlantedMethodConsumer: IPlantedPort&",
                Planted + "PlantedPortSink: IPlantedPort",
                Planted + "PlantedWrappedConsumer: Lazy<IPlantedPort>");

    [TestMethod]
    public void ParametersMentioning_NeverReportsTheNegativeControl()
        => ThisAssembly.ParametersMentioning(type => type == typeof(IPlantedPort))
            .Should().NotContain(hit => hit.Contains("PlantedUnrelatedConsumer", StringComparison.Ordinal));

    [TestMethod]
    public void InjectedParameters_CoverConstructorsAndStaticMethodsWithTheirMember()
    {
        InjectedParameter[] parameters = [.. ThisAssembly.InjectedParameters()];

        InjectedParameter constructor = parameters.Single(parameter => parameter.Owner == typeof(PlantedBareConsumer)
            && parameter.ParameterType == typeof(IPlantedPort));
        constructor.Member.Should().BeAssignableTo<ConstructorInfo>();
        constructor.Parameter.Name.Should().Be("port");
        constructor.ToString().Should().Be(Planted + "PlantedBareConsumer: IPlantedPort");

        InjectedParameter method = parameters.Single(parameter => parameter.Owner == typeof(PlantedMethodConsumer));
        method.Member.Name.Should().Be(nameof(PlantedMethodConsumer.Use));
        method.ParameterType.IsByRef.Should().BeTrue();
    }

    [TestMethod]
    public void InjectedParameters_IncludeCompilerGeneratedNestedTypes()
        => ThisAssembly.InjectedParameters()
            .Should().Contain(parameter => parameter.Owner.IsNested && parameter.Owner.Name.Contains('<', StringComparison.Ordinal),
                "an async state machine or closure is where a naive scan misses a dependency");

    [TestMethod]
    public void InjectedParameters_SkipInterfaces()
        => ThisAssembly.InjectedParameters().Should().NotContain(parameter => parameter.Owner.IsInterface);

    [TestMethod]
    public void Mentions_ACyclicTypeGraph_Terminates()
        => typeof(string).Mentions(type => type == typeof(IPlantedPort)).Should().BeFalse(
            "string implements IComparable<string>, a cycle back to itself");

    [TestMethod]
    public void Mentions_TheFencedTypeItself_IsTrue()
        => typeof(IPlantedPort).Mentions(type => type == typeof(IPlantedPort)).Should().BeTrue();

    [TestMethod]
    public void StaticMembers_CatchesThePlantedViolationsAndLetsTheAllowedFormsThrough()
        => ThisAssembly.StaticMembers()
            .Where(hit => hit.StartsWith(Planted + "PlantedStatic", StringComparison.Ordinal))
            .Should().Equal(
                [
                    Planted + "PlantedStaticMembers.Changed",
                    Planted + "PlantedStaticMembers.Counter",
                    Planted + "PlantedStaticMembers.HiddenSettable",
                    Planted + "PlantedStaticMembers.Settable",
                    Planted + "PlantedStaticMembers.Shared",
                    Planted + "PlantedStaticMembers.Shout",
                    Planted + "PlantedStaticMembers.Twice",
                    Planted + "PlantedStaticMembers.WriteOnly",
                    Planted + "PlantedStaticMembers.hiddenCounter",
                ],
                "the mutable fields, the event, the non-private properties (a write-only one too) and methods and the private settable "
                + "property are caught; the constant, the readonly field, the operator, the private method and "
                + "the extension method are allowed");

    [TestMethod]
    public void StaticMembers_WithAnExemption_SkipsTheExemptTypes()
        => ThisAssembly.StaticMembers(type => type == typeof(PlantedStaticMembers))
            .Should().NotContain(hit => hit.StartsWith(Planted + "PlantedStaticMembers.", StringComparison.Ordinal));

    [TestMethod]
    public void StaticMembers_SkipCompilerGeneratedTypes()
        => ThisAssembly.StaticMembers().Should().NotContain(hit => hit.Contains('<', StringComparison.Ordinal));

    [TestMethod]
    public void InternalTypesOutsideInternalNamespaces_CatchesEveryPlantedKind()
        => ThisAssembly.InternalTypesOutsideInternalNamespaces()
            .Where(hit => hit.Contains(".Fences.", StringComparison.Ordinal))
            .Should().Equal(
                "Nordstein.Core.Testing.Tests.Fences.IPlantedMisplaced",
                "Nordstein.Core.Testing.Tests.Fences.Internalised.PlantedLookalike",
                "Nordstein.Core.Testing.Tests.Fences.PlantedMisplacedCallback",
                "Nordstein.Core.Testing.Tests.Fences.PlantedMisplacedClass",
                "Nordstein.Core.Testing.Tests.Fences.PlantedMisplacedKind",
                "Nordstein.Core.Testing.Tests.Fences.PlantedMisplacedRecord");

    [TestMethod]
    public void InternalTypesOutsideInternalNamespaces_CatchesAGlobalNamespaceType()
        => ThisAssembly.InternalTypesOutsideInternalNamespaces().Should().Contain("PlantedGlobalType");

    [TestMethod]
    public void InternalTypesOutsideInternalNamespaces_LetsTheAllowedFormsThrough()
    {
        string[] hits = ThisAssembly.InternalTypesOutsideInternalNamespaces();

        hits.Should().NotContain(hit => hit.Contains("PlantedPublicHost", StringComparison.Ordinal), "nested types live in their host's file");
        hits.Should().NotContain(hit => hit.Contains("PlantedFileLocal", StringComparison.Ordinal), "a file-local type is private to its file");
        hits.Should().NotContain(hit => hit.Contains('<', StringComparison.Ordinal), "compiler-generated types have no folder");
        hits.Should().NotContain(hit => hit.StartsWith(Planted, StringComparison.Ordinal), "the Internal namespace is where internal types belong");
    }

    [TestMethod]
    public void InternalTypesOutsideInternalNamespaces_WithAnExemption_SkipsIt()
        => ThisAssembly.InternalTypesOutsideInternalNamespaces(type => type == typeof(PlantedMisplacedClass))
            .Should().NotContain("Nordstein.Core.Testing.Tests.Fences.PlantedMisplacedClass")
            .And.Contain("Nordstein.Core.Testing.Tests.Fences.PlantedMisplacedKind");

    [TestMethod]
    public void ReferencedMemberNames_FindAReferenceMadeOnlyInsideAnAsyncStateMachine()
        => ThisAssembly.ReferencedMemberNames().Should().Contain("System.Net.WebUtility::HtmlEncode");

    [TestMethod]
    public void ReferencedMemberNames_NameTheGenericTypeOfAnInstantiationsMember()
        => ThisAssembly.ReferencedMemberNames().Should().Contain("System.Collections.Generic.List`1::Add");

    [TestMethod]
    public void ReferencedMemberNames_NameTheGenericTypeOfThisAssemblysInstantiations()
        => ThisAssembly.ReferencedMemberNames().Should().Contain(
            [
                Planted + "PlantedConstrainedConsumer`1::.ctor",
                Planted + "PlantedConstrainedConsumer`1::get_Port",
                "Nordstein.Core.Testing.Tests.Logging.Internal.Outer`1+Inner::.ctor",
            ]);

    [TestMethod]
    public void ReferencedMemberNames_NameOtherSpecificationsAndVarargCallSites()
        => ThisAssembly.ReferencedMemberNames().Should().Contain(
            ["<Array>::.ctor", Planted + "PlantedReferences::Varargs"]);

    [TestMethod]
    public void ReferencedTypeNames_FindATypeReferencedOnlyByTypeof()
        => ThisAssembly.ReferencedTypeNames().Should().Contain("System.Net.Sockets.SocketFlags");

    [TestMethod]
    public void ReferencedTypeNames_NameNestedTypesWithAPlus()
        => typeof(FenceScanExtensions).Assembly.ReferencedTypeNames()
            .Should().Contain("Nordstein.Core.Common.Lifecycle.ITempDirectory+Create", "the harness module resolves that nested delegate")
            .And.OnlyHaveUniqueItems().And.BeInAscendingOrder(StringComparer.Ordinal);

    [TestMethod]
    public void ReferencedMemberNames_AreUniqueAndSorted()
        => ThisAssembly.ReferencedMemberNames().Should().OnlyHaveUniqueItems().And.BeInAscendingOrder(StringComparer.Ordinal);

    [TestMethod]
    public void ReferenceScans_OfAnAssemblyWithoutAFile_Throw()
    {
        Assembly inMemory = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Nordstein.Core.Testing.Tests.Dynamic"), System.Reflection.Emit.AssemblyBuilderAccess.Run);

        Action members = () => inMemory.ReferencedMemberNames();
        Action types = () => inMemory.ReferencedTypeNames();

        members.Should().Throw<InvalidOperationException>().WithMessage("*has no file*");
        types.Should().Throw<InvalidOperationException>().WithMessage("*has no file*");
    }

    [TestMethod]
    public void ReferenceScans_OfAnAssemblyLoadedFromBytes_Throw()
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("fence-scan-bytes", isCollectible: true);
        try
        {
            using FileStream image = File.OpenRead(typeof(FenceScanExtensions).Assembly.Location);
            Assembly fromBytes = context.LoadFromStream(image);

            Action members = () => fromBytes.ReferencedMemberNames();

            fromBytes.Location.Should().BeEmpty();
            members.Should().Throw<InvalidOperationException>().WithMessage("*has no file*");
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    [DataRow(typeof(int), "Int32")]
    [DataRow(typeof(int[]), "Int32[]")]
    [DataRow(typeof(int[,]), "Int32[,]")]
    [DataRow(typeof(List<string>), "List<String>")]
    [DataRow(typeof(Dictionary<string, List<int>>), "Dictionary<String, List<Int32>>")]
    [DataRow(typeof(Outer<int>.Inner), "Inner<Int32>")]
    [DataRow(typeof(List<>), "List<T>")]
    [DataRow(typeof(Lazy<IPlantedPort>[]), "Lazy<IPlantedPort>[]")]
    public void Display_SpellsTheTypeAsCSharpWithoutNamespaces(Type type, string expected)
        => type.Display().Should().Be(expected);

    [TestMethod]
    public void Display_ByRefAndPointerTypes_KeepTheirMarker()
    {
        typeof(int).MakeByRefType().Display().Should().Be("Int32&");
        typeof(int).MakePointerType().Display().Should().Be("Int32*");
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
#pragma warning disable CS8600, CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action parameters = () => ((Assembly)null).InjectedParameters().ToArray();
        Action mentioning = () => ThisAssembly.ParametersMentioning(null);
        Action mentions = () => typeof(int).Mentions(null);
        Action mentionsOfNull = () => ((Type)null).Mentions(_ => true);
        Action statics = () => ((Assembly)null).StaticMembers();
        Action internals = () => ((Assembly)null).InternalTypesOutsideInternalNamespaces();
        Action members = () => ((Assembly)null).ReferencedMemberNames();
        Action types = () => ((Assembly)null).ReferencedTypeNames();
        Action display = () => ((Type)null).Display();
#pragma warning restore CS8600, CS8625

        parameters.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        mentioning.Should().Throw<ArgumentNullException>().WithParameterName("isFenced");
        mentions.Should().Throw<ArgumentNullException>().WithParameterName("isFenced");
        mentionsOfNull.Should().Throw<ArgumentNullException>().WithParameterName("type");
        statics.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        internals.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        members.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        types.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        display.Should().Throw<ArgumentNullException>().WithParameterName("type");
    }
}
