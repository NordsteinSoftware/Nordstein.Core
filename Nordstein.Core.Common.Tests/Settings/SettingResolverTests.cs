using Autofac;
using Autofac.Core;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Settings;
using Nordstein.Core.Common.Tests.Settings.Internal;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests.Settings;

/// <summary>
/// The wildcard-template resolver: a concrete setting without asking the configuration, a template's
/// instance only for a configured key (spelled as configured), and every concrete setting of the
/// requested scopes with each template expanded per configured key.
/// </summary>
[TestClass]
public sealed class SettingResolverTests : BaseTest<Module>
{
    private static readonly SettingNaming Naming = new("$APP__", "$USER__", "$LOCAL__");

    private static readonly SettingRoot[] Roots =
    [
        new("Stations:*", typeof(StationOptions)),
        new("Theme", typeof(ThemeOptions)),
        new("Stations:*:Extra", typeof(StationExtraOptions)),
    ];

    // A wildcard first segment makes every top-level key an element, so it shares a registry only
    // with roots whose keys cannot also be its elements' sections.
    private static readonly SettingRoot[] RootLevelRoots =
    [
        new("Theme", typeof(ThemeOptions)),
        new("*", typeof(ZoneOptions)),
    ];

    [TestMethod]
    public void Resolve_ConcreteSetting_IsTheRegistrysWithoutAskingTheConfiguration()
    {
        ISettingCollections collections = Collections();
        (ISettingResolver resolver, ISettingRegistry registry) = Resolver(collections);

        SettingDescriptor? found = resolver.Resolve("$app__theme__ACCENT");

        found.Should().BeSameAs(registry.Find("$APP__Theme__Accent"));
        collections.ReceivedCalls().Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_ConfiguredElement_IsTheInstanceSpelledAsConfigured()
    {
        ISettingCollections collections = Collections(("Stations", ["North", "South"]));
        (ISettingResolver resolver, _) = Resolver(collections);

        SettingDescriptor? found = resolver.Resolve("$APP__Stations__north__Url");

        found.Should().BeEquivalentTo(new { Name = "$APP__Stations__North__Url", Path = "Stations:North:Url", Scope = SettingScope.Global, Type = typeof(Uri), IsTemplate = false });
        collections.Received(1).KeysOf("Stations");
    }

    [TestMethod]
    public void Resolve_ElementNotConfigured_IsNull()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["North"])));

        resolver.Resolve("$APP__Stations__East__Url").Should().BeNull();
    }

    [TestMethod]
    public void Resolve_UnknownName_IsNullWithoutAskingTheConfiguration()
    {
        ISettingCollections collections = Collections();
        (ISettingResolver resolver, _) = Resolver(collections);

        resolver.Resolve("$APP__Nothing__Here").Should().BeNull();
        collections.ReceivedCalls().Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_TemplateAtTheConfigurationRoot_AsksAboutTheRoot()
    {
        ISettingCollections collections = Collections((string.Empty, ["Inner", "Outer"]));
        (ISettingResolver resolver, _) = Resolver(collections, RootLevelRoots);

        resolver.Resolve("$APP__outer__Label").Should().BeEquivalentTo(new { Path = "Outer:Label" });
        resolver.Expand([SettingScope.Global]).Select(descriptor => descriptor.Path).Should().Equal("Theme:Accent", "Inner:Label", "Outer:Label");
        collections.Received(2).KeysOf(string.Empty);
    }

    [TestMethod]
    public void Resolve_NestedCollectionTemplate_AsksAboutItsCollection()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["North"])));

        resolver.Resolve("$APP__Stations__North__Extra__Owner").Should().BeEquivalentTo(new { Path = "Stations:North:Extra:Owner" });
    }

    [TestMethod]
    public void Resolve_UserTemplate_ResolvesWithTheUserPrefix()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["North"])));

        resolver.Resolve("$USER__Stations__North__Followed").Should().BeEquivalentTo(new { Name = "$USER__Stations__North__Followed", Scope = SettingScope.User });
    }

    [TestMethod]
    public void Expand_GlobalAndUser_ListsInRegistryOrderWithEachElementsSettingsTogether()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["North", "South"])));

        IReadOnlyList<SettingDescriptor> expanded = resolver.Expand([SettingScope.Global, SettingScope.User]);

        expanded.Select(descriptor => descriptor.Path).Should().Equal(
            "Stations:North:Url",
            "Stations:North:Interval",
            "Stations:North:Followed",
            "Stations:South:Url",
            "Stations:South:Interval",
            "Stations:South:Followed",
            "Theme:Accent",
            "Theme:FontSize",
            "Stations:North:Extra:Owner",
            "Stations:South:Extra:Owner");
        expanded.Should().OnlyContain(descriptor => !descriptor.IsTemplate);
    }

    [TestMethod]
    public void Expand_UserOnly_NeverAsksAboutGlobalCollections()
    {
        ISettingCollections collections = Collections(("Stations", ["North"]));
        (ISettingResolver resolver, _) = Resolver(collections);

        IReadOnlyList<SettingDescriptor> expanded = resolver.Expand([SettingScope.User]);

        expanded.Select(descriptor => descriptor.Name).Should().Equal("$USER__Stations__North__Followed", "$USER__Theme__FontSize");
        collections.Received(1).KeysOf("Stations");
        collections.ReceivedCalls().Should().HaveCount(1, "the global templates of Stations:*:Extra are not asked about");
    }

    [TestMethod]
    public void Expand_LocalOnly_ListsTheLocalSettings()
    {
        (ISettingResolver resolver, _) = Resolver(Collections());

        resolver.Expand([SettingScope.Local]).Select(descriptor => descriptor.Path).Should().Equal("Theme:Density");
    }

    [TestMethod]
    public void Expand_NoScope_ListsNothing()
    {
        ISettingCollections collections = Collections(("Stations", ["North"]));
        (ISettingResolver resolver, _) = Resolver(collections);

        resolver.Expand([]).Should().BeEmpty();
        collections.ReceivedCalls().Should().BeEmpty();
    }

    [TestMethod]
    public void Expand_NothingConfigured_LeavesOnlyConcreteSettings()
    {
        (ISettingResolver resolver, _) = Resolver(Collections());

        resolver.Expand([SettingScope.Global]).Select(descriptor => descriptor.Path).Should().Equal("Theme:Accent");
    }

    [TestMethod]
    public void Expand_KeysWithoutAName_AreLeftOutAndDuplicatesListedOnce()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["", "*", "a:b", "a__b", "_a", "a_", "Ok", "OK", "x_y"])));

        resolver.Expand([SettingScope.User]).Select(descriptor => descriptor.Path).Should().Equal(
            "Stations:Ok:Followed", "Stations:x_y:Followed", "Theme:FontSize");
    }

    [TestMethod]
    public void Expand_SameCollectionInTwoApartRuns_AsksOncePerRun()
    {
        ISettingCollections collections = Collections(("Stations", ["North"]));
        (ISettingResolver resolver, _) = Resolver(collections);

        resolver.Expand([SettingScope.Global]);

        collections.Received(2).KeysOf("Stations");
    }

    [TestMethod]
    public void Expand_UndefinedScope_Throws()
    {
        (ISettingResolver resolver, _) = Resolver(Collections());

        Action act = () => resolver.Expand([(SettingScope)42]);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("scopes");
    }

    [TestMethod]
    public void Expand_CollectionsReturningNull_Throws()
    {
        ISettingCollections collections = Substitute.For<ISettingCollections>();
        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a faulty product implementation would return.
        collections.KeysOf(Arg.Any<string>()).Returns((new IReadOnlyList<string>[1])[0]);
        (ISettingResolver resolver, _) = Resolver(collections);

        FluentActions.Invoking(() => resolver.Expand([SettingScope.Global])).Should().Throw<InvalidOperationException>().WithMessage("*ISettingCollections*");
        FluentActions.Invoking(() => resolver.Resolve("$APP__Stations__North__Url")).Should().Throw<InvalidOperationException>().WithMessage("*ISettingCollections*");
    }

    [TestMethod]
    public void Expand_CollectionsReturningANullKey_Throws()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["North", (new string[1])[0]])));

        FluentActions.Invoking(() => resolver.Expand([SettingScope.Global])).Should().Throw<InvalidOperationException>().WithMessage("*ISettingCollections*");
        FluentActions.Invoking(() => resolver.Resolve("$APP__Stations__South__Url")).Should().Throw<InvalidOperationException>().WithMessage("*ISettingCollections*");
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
        (ISettingResolver resolver, _) = Resolver(Collections());

        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        FluentActions.Invoking(() => resolver.Resolve((new string[1])[0])).Should().Throw<ArgumentNullException>().WithParameterName("name");
        FluentActions.Invoking(() => resolver.Expand((new IEnumerable<SettingScope>[1])[0])).Should().Throw<ArgumentNullException>().WithParameterName("scopes");
    }

    [TestMethod]
    public void Module_WithoutSettingCollections_DoesNotResolveTheResolver()
    {
        ILifetimeScope scope = GetServices(builder =>
        {
            builder.RegisterInstance(Naming);
            builder.RegisterInstance(Roots[1]);
        }).GetRequiredService<ILifetimeScope>();

        Action act = () => scope.Resolve<ISettingResolver>();

        act.Should().Throw<DependencyResolutionException>();
    }

    [TestMethod]
    public void Resolver_ConcurrentCallers_GetTheSameSettings()
    {
        (ISettingResolver resolver, _) = Resolver(Collections(("Stations", ["North", "South"])));
        string[] expected = [.. resolver.Expand([SettingScope.Global, SettingScope.User]).Select(descriptor => descriptor.Name)];

        var results = new string[16][];
        Parallel.For(0, results.Length, worker => results[worker] = [.. resolver.Expand([SettingScope.Global, SettingScope.User]).Select(descriptor => descriptor.Name)]);

        results.Should().AllSatisfy(result => result.Should().Equal(expected));
    }

    private static ISettingCollections Collections(params (string Path, string[] Keys)[] configured)
    {
        ISettingCollections collections = Substitute.For<ISettingCollections>();
        collections.KeysOf(Arg.Any<string>()).Returns(Array.Empty<string>());
        foreach ((string path, string[] keys) in configured)
        {
            collections.KeysOf(path).Returns(keys);
        }

        collections.ClearReceivedCalls();
        return collections;
    }

    private (ISettingResolver Resolver, ISettingRegistry Registry) Resolver(ISettingCollections collections, SettingRoot[]? roots = null)
    {
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(Naming);
            foreach (SettingRoot root in roots ?? Roots)
            {
                builder.RegisterInstance(root);
            }

            builder.RegisterInstance(collections).As<ISettingCollections>();
        });

        return (services.GetRequiredService<ISettingResolver>(), services.GetRequiredService<ISettingRegistry>());
    }
}
