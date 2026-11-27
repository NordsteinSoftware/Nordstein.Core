using System.Globalization;
using Autofac;
using Autofac.Core;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Settings;
using Nordstein.Core.Common.Tests.Settings.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Settings;

[TestClass]
public sealed class SettingRegistryTests : BaseTest<Module>
{
    private static readonly SettingNaming Naming = new("$APP__", "$USER__", "$LOCAL__");

    [TestMethod]
    public void SettingName_EnvStyle_CanonicalisedCaseInsensitive()
    {
        ISettingRegistry registry = SampleRegistry();

        SettingDescriptor? found = registry.Find("$app__greeting__TEXT");

        found.Should().NotBeNull();
        found.Should().BeEquivalentTo(new
        {
            Name = "$APP__Greeting__Text",
            Path = "Greeting:Text",
            Scope = SettingScope.Global,
            Type = typeof(string),
            Description = "The greeting's text.",
            IsProtected = false,
            IsSink = false,
            IsWritable = true,
            DefaultValue = "Hello",
            IsTemplate = false,
        });
    }

    [TestMethod]
    public void SettingRegistry_ProtectedProperty_DescriptorIsProtected()
    {
        ISettingRegistry registry = SampleRegistry();

        SettingDescriptor? password = registry.Find("$APP__Mailer__Password");
        SettingDescriptor? host = registry.Find("$APP__Mailer__Host");
        SettingDescriptor? port = registry.Find("$APP__Mailer__Port");

        password.Should().BeEquivalentTo(new { IsProtected = true, IsSink = true });
        host.Should().BeEquivalentTo(new { IsProtected = false, IsSink = true });
        port.Should().BeEquivalentTo(new { IsProtected = false, IsSink = false });
    }

    [TestMethod]
    [DataRow("$APP__Greeting__Unmarked", DisplayName = "unmarked property")]
    [DataRow("$APP__Weather__Text", DisplayName = "unknown section")]
    [DataRow("$USER__Greeting__Text", DisplayName = "global key under the user prefix")]
    [DataRow("$LOCAL__Mailer__Port", DisplayName = "global key under the local prefix")]
    [DataRow("$OTHER__Greeting__Text", DisplayName = "unknown prefix")]
    [DataRow("Greeting__Text", DisplayName = "no prefix")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("$APP__Greeting", DisplayName = "a root")]
    [DataRow("$APP__Mailer__Retry", DisplayName = "a section")]
    [DataRow("$APP__Mailer__Retry__Attempts__Extra", DisplayName = "below a setting")]
    [DataRow("$APP__Feeds__News", DisplayName = "a template root's element")]
    [DataRow(" $APP__Greeting__Text", DisplayName = "leading space")]
    public void SettingRegistry_UnregisteredKey_FindReturnsNull(string name)
        => SampleRegistry().Find(name).Should().BeNull();

    [TestMethod]
    public void SettingRegistry_NestedRecord_PathJoinsPropertyNames()
    {
        SettingDescriptor? attempts = SampleRegistry().Find("$APP__Mailer__Retry__Attempts");

        attempts.Should().BeEquivalentTo(new
        {
            Name = "$APP__Mailer__Retry__Attempts",
            Path = "Mailer:Retry:Attempts",
            Type = typeof(int),
            DefaultValue = "3",
        });
    }

    [TestMethod]
    public void SettingRegistry_InheritedProperty_Registered()
    {
        IEnumerable<string> greetingPaths = SampleRegistry().All
            .Select(descriptor => descriptor.Path)
            .Where(path => path.StartsWith("Greeting:", StringComparison.Ordinal));

        greetingPaths.Should().Equal("Greeting:Text", "Greeting:Tone", "Greeting:Repeat", "Greeting:Theme");
    }

    [TestMethod]
    public void SettingRegistry_OverriddenProperty_ListedOnceAtTheBasePosition()
    {
        ISettingRegistry registry = Registry(Naming, new SettingRoot("Label", typeof(LabelOptions)));

        registry.All.Select(descriptor => descriptor.Path).Should().Equal("Label:Label", "Label:Size");
        registry.Find("$APP__Label__Label").Should().BeEquivalentTo(new
        {
            Description = "The label's text.",
            DefaultValue = "derived",
        });
    }

    [TestMethod]
    public void SettingRegistry_WildcardRoot_FindCarriesTheGivenSegment()
    {
        ISettingRegistry registry = SampleRegistry();

        SettingDescriptor? news = registry.Find("$APP__Feeds__News__Url");
        SettingDescriptor? wire = registry.Find("$app__FEEDS__news-Wire_1__url");
        SettingDescriptor template = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Url");

        news.Should().BeEquivalentTo(new
        {
            Name = "$APP__Feeds__News__Url",
            Path = "Feeds:News:Url",
            Type = typeof(Uri),
            IsSink = true,
            IsTemplate = false,
        });
        wire.Should().BeEquivalentTo(new { Name = "$APP__Feeds__news-Wire_1__Url", Path = "Feeds:news-Wire_1:Url" });
        template.Should().BeEquivalentTo(new { Name = "$APP__Feeds__*__Url", IsTemplate = true });
    }

    [TestMethod]
    [DataRow("$APP__Feeds___News__Url", DisplayName = "segment starts with an underscore")]
    [DataRow("$APP__Feeds__News___Url", DisplayName = "three underscores after the segment")]
    [DataRow("$APP__Feeds__*__Url", DisplayName = "the wildcard itself")]
    [DataRow("$APP__Feeds____Url", DisplayName = "empty segment")]
    [DataRow("$APP__Feeds__News:Wire__Url", DisplayName = "colon in the segment")]
    public void SettingRegistry_TemplateNameWithBadSegment_NotFound(string name)
        => SampleRegistry().Find(name).Should().BeNull();

    [TestMethod]
    public void SettingRegistry_Instantiate_ReplacesTheWildcard()
    {
        ISettingRegistry registry = SampleRegistry();
        SettingDescriptor template = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Limit");

        SettingDescriptor concrete = registry.Instantiate(template, "Wire_1");

        concrete.Should().Be(template with { Name = "$APP__Feeds__Wire_1__Limit", Path = "Feeds:Wire_1:Limit" });
        concrete.IsTemplate.Should().BeFalse();
        concrete.Should().BeEquivalentTo(new { IsWritable = false, DefaultValue = "20" });
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("a:b")]
    [DataRow("a__b")]
    [DataRow("_a")]
    [DataRow("a_")]
    [DataRow("*")]
    public void SettingRegistry_Instantiate_RejectsBadSegments(string segment)
    {
        ISettingRegistry registry = SampleRegistry();
        SettingDescriptor template = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Url");

        Action instantiate = () => registry.Instantiate(template, segment);

        instantiate.Should().Throw<ArgumentException>().WithParameterName("segment");
    }

    [TestMethod]
    public void SettingRegistry_Instantiate_TemplateLessDescriptor_Throws()
    {
        ISettingRegistry registry = SampleRegistry();
        SettingDescriptor port = registry.All.Single(descriptor => descriptor.Path == "Mailer:Port");
        SettingDescriptor instance = registry.Instantiate(
            registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Url"), "News");

        Action fromConcrete = () => registry.Instantiate(port, "News");
        Action fromInstance = () => registry.Instantiate(instance, "Wire");

        fromConcrete.Should().Throw<ArgumentException>().WithParameterName("template");
        fromInstance.Should().Throw<ArgumentException>().WithParameterName("template");
    }

    [TestMethod]
    public void SettingRegistry_Instantiate_ForeignTemplate_Throws()
    {
        ISettingRegistry registry = SampleRegistry();
        SettingDescriptor template = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Url");

        Action instantiate = () => registry.Instantiate(template with { Description = "Changed." }, "News");

        instantiate.Should().Throw<ArgumentException>().WithParameterName("template");
    }

    [TestMethod]
    public void SettingRegistry_Instantiate_Null_ThrowsArgumentNull()
    {
        ISettingRegistry registry = SampleRegistry();
        SettingDescriptor template = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Url");
        SettingDescriptor missingTemplate = (new SettingDescriptor[1])[0];
        string missingSegment = (new string[1])[0];

        Action withoutTemplate = () => registry.Instantiate(missingTemplate, "News");
        Action withoutSegment = () => registry.Instantiate(template, missingSegment);

        withoutTemplate.Should().Throw<ArgumentNullException>().WithParameterName("template");
        withoutSegment.Should().Throw<ArgumentNullException>().WithParameterName("segment");
    }

    [TestMethod]
    public void SettingRegistry_ReadOnlyMember_NotWritable()
    {
        ISettingRegistry registry = SampleRegistry();

        registry.Find("$APP__Mailer__Sender").Should().BeEquivalentTo(new { IsWritable = false });
        registry.Find("$APP__Mailer__Retry__Attempts").Should().BeEquivalentTo(new { IsWritable = false });
        registry.Find("$APP__Mailer__Retry__Delay").Should().BeEquivalentTo(new { IsWritable = true });
        registry.Find("$APP__Mailer__Port").Should().BeEquivalentTo(new { IsWritable = true });
        registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Limit").IsWritable.Should().BeFalse();
        registry.Find("$APP__Feeds__News__Limit").Should().BeEquivalentTo(new { IsWritable = false });
        registry.Find("$APP__Feeds__News__Url").Should().BeEquivalentTo(new { IsWritable = true });
    }

    [TestMethod]
    [DataRow("Missing", DisplayName = "no such member")]
    [DataRow("Retry", DisplayName = "a section")]
    [DataRow("Mailer:Port", DisplayName = "the root path repeated")]
    [DataRow("", DisplayName = "empty")]
    public void SettingRegistry_ReadOnlyEntryNamingNoSetting_Throws(string entry)
    {
        InvalidOperationException refusal = Refusal(
            Naming, new SettingRoot("Mailer", typeof(MailerOptions), ["Port", entry]));

        refusal.Message.Should().ContainAll("Mailer", nameof(MailerOptions), $"'{entry}'");
    }

    [TestMethod]
    public void SettingRegistry_DefaultValues_FormattedInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            ISettingRegistry registry = SampleRegistry();

            DefaultOf(registry, "Mailer:Backoff").Should().Be("2.5");
            DefaultOf(registry, "Mailer:Enabled").Should().Be("true");
            DefaultOf(registry, "Mailer:Timeout").Should().Be("00:01:30");
            DefaultOf(registry, "Mailer:Recipients").Should().Be("[\"*\"]");
            DefaultOf(registry, "Greeting:Tone").Should().Be("Warm");
            DefaultOf(registry, "Mailer:Port").Should().Be("25");
            DefaultOf(registry, "Mailer:MaxBytes").Should().Be("10485760");
            DefaultOf(registry, "Mailer:Relay").Should().Be("https://relay.example/v1");
            DefaultOf(registry, "Greeting:Text").Should().Be("Hello");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void SettingRegistry_DefaultValues_EdgeValuesFormattedInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            ISettingRegistry registry = Registry(Naming, new SettingRoot("Format", typeof(FormatOptions)));

            DefaultOf(registry, "Format:Ratio").Should().Be("NaN");
            DefaultOf(registry, "Format:Offset").Should().Be("-3");
            DefaultOf(registry, "Format:Big").Should().Be("-1234567890123");
            DefaultOf(registry, "Format:Grace").Should().Be("-1.02:03:04");
            DefaultOf(registry, "Format:Limit").Should().Be("7");
            DefaultOf(registry, "Format:Patterns").Should().Be("[\"a\\u002Bb\",\"\\u003Cx\\u003E\",\"\\u00E9\",null]");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void SettingRegistry_EnumValueTypeOverride_UsesTheEnum()
    {
        ISettingRegistry registry = Registry(Naming, new SettingRoot("Format", typeof(FormatOptions)));

        SettingDescriptor toneName = registry.All.Single(descriptor => descriptor.Path == "Format:ToneName");

        toneName.Type.Should().Be(typeof(GreetingTone));
        toneName.DefaultValue.Should().BeNull("the configured string is not a value of the enum type");
    }

    [TestMethod]
    public void SettingRegistry_ProtectedSetting_HasNoDefault()
    {
        ISettingRegistry registry = Registry(Naming, new SettingRoot("Format", typeof(FormatOptions)));

        registry.Find("$APP__Format__Token").Should().BeEquivalentTo(new { IsProtected = true, DefaultValue = (string?)null });
    }

    [TestMethod]
    public void SettingRegistry_ProtectedAndSinkOnOverrideOnly_Count()
    {
        ISettingRegistry registry = Registry(Naming, new SettingRoot("Access", typeof(KeyOptions)));

        registry.All.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Path = "Access:Key",
            Description = "The access key.",
            IsProtected = true,
            IsSink = true,
            DefaultValue = (string?)null,
        });
    }

    [TestMethod]
    public void SettingRegistry_HiddenMarkedProperty_PathsCollide()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(CodeOptions)));

        refusal.Message.Should().ContainAll($"{nameof(CodeBase)}.Code", $"{nameof(CodeOptions)}.Code");
    }

    [TestMethod]
    public void SettingRegistry_SettingAndSectionOnOneKey_Throws()
    {
        InvalidOperationException refusal = Refusal(
            Naming,
            new SettingRoot("Mailer", typeof(MailerOptions)),
            new SettingRoot("Mailer", typeof(RetryCountOptions)));

        refusal.Message.Should().ContainAll("'Mailer:Retry:Attempts'", "'Mailer:Retry'", "both a setting and a section");
    }

    [TestMethod]
    public void SettingRegistry_StructRootAndNullableStructSections_ReadTheirDefaults()
    {
        ISettingRegistry registry = Registry(
            Naming,
            new SettingRoot("Window", typeof(WindowOptions)),
            new SettingRoot("Screen", typeof(ScreenOptions)));

        registry.All.Select(descriptor => descriptor.Path).Should().Equal("Window:Width", "Screen:Window:Width", "Screen:Spare:Width");
        DefaultOf(registry, "Window:Width").Should().Be("80");
        DefaultOf(registry, "Screen:Window:Width").Should().Be("80");
        DefaultOf(registry, "Screen:Spare:Width").Should().BeNull("the section is null in the default instance");
    }

    [TestMethod]
    public void SettingRegistry_AbstractRoot_HasNoDefaults()
    {
        ISettingRegistry registry = Registry(Naming, new SettingRoot("Base", typeof(GreetingBase)));

        registry.Find("$APP__Base__Text").Should().BeEquivalentTo(new { DefaultValue = (string?)null });
    }

    [TestMethod]
    public void SettingRegistry_GroupingRecordWithoutOwnSettings_IsASection()
    {
        ISettingRegistry registry = Registry(Naming, new SettingRoot("Outer", typeof(OuterOptions)));

        registry.All.Select(descriptor => descriptor.Path).Should().Equal("Outer:Middle:Retry:Attempts", "Outer:Middle:Retry:Delay");
        DefaultOf(registry, "Outer:Middle:Retry:Attempts").Should().Be("3");
    }

    [TestMethod]
    public void SettingRegistry_SectionWithOnlyNonPublicMarkings_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(HiddenSectionOptions)));

        refusal.Message.Should().ContainAll(nameof(HiddenOnly), "Secret");
    }

    [TestMethod]
    [DataRow(typeof(ThrowingConstructorOptions), "constructor")]
    [DataRow(typeof(ThrowingGetterOptions), "getter")]
    public void SettingRegistry_ThrowingDefaultRead_ThrowsNamingTheMember(Type record, string part)
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", record));

        refusal.Message.Should().ContainAll(record.Name, part, "threw");
        refusal.InnerException.Should().BeOfType<NotSupportedException>();
    }

    [TestMethod]
    [DataRow(typeof(IDisposable), DisplayName = "interface")]
    [DataRow(typeof(List<>), DisplayName = "open generic")]
    public void SettingRegistry_UnusableRecordType_Throws(Type record)
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", record));

        refusal.Message.Should().ContainAll("'Sample'", "usable record type");
    }

    [TestMethod]
    public void SettingRegistry_NullReadOnlyEntry_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Mailer", typeof(MailerOptions), new string[1]));

        refusal.Message.Should().ContainAll("read-only entry", "'Mailer'", nameof(MailerOptions));
    }

    [TestMethod]
    public void SettingRegistry_NoDefault_DefaultValueNull()
    {
        ISettingRegistry registry = Registry(
            Naming,
            new SettingRoot("Mailer", typeof(MailerOptions)),
            new SettingRoot("Feeds:*", typeof(FeedOptions)),
            new SettingRoot("Sized", typeof(SizedOptions)));

        DefaultOf(registry, "Mailer:Sender").Should().BeNull("a required member has no default");
        DefaultOf(registry, "Mailer:Password").Should().BeNull("a protected member shows no default");
        DefaultOf(registry, "Mailer:Retry:Delay").Should().BeNull("a nullable value type left unset has none");
        DefaultOf(registry, "Feeds:*:Url").Should().BeNull("a required member has no default");
        DefaultOf(registry, "Sized:Size").Should().BeNull("a record without a parameterless constructor has none");
    }

    [TestMethod]
    public void SettingRegistry_ValueTypeOverride_UsesIt()
    {
        ISettingRegistry registry = SampleRegistry();

        SettingDescriptor proxy = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Proxy");

        proxy.Type.Should().Be(typeof(string));
        proxy.DefaultValue.Should().BeNull("the record instance is not a string");
        registry.All.Should().NotContain(descriptor => descriptor.Path.StartsWith("Feeds:*:Proxy:", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(typeof(UnsupportedDecimalOptions), "Price")]
    [DataRow(typeof(UnsupportedArrayOptions), "Names")]
    [DataRow(typeof(UnsupportedListOptions), "Numbers")]
    [DataRow(typeof(UnsupportedValueTypeOptions), "Moment")]
    public void SettingRegistry_UnsupportedType_Throws(Type record, string property)
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", record));

        refusal.Message.Should().ContainAll(record.Name, property);
    }

    [TestMethod]
    [DataRow(typeof(ProtectedWithoutSettingOptions), "Secret")]
    [DataRow(typeof(SinkWithoutSettingOptions), "Target")]
    public void SettingRegistry_ProtectedWithoutSetting_Throws(Type record, string property)
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", record));

        refusal.Message.Should().ContainAll(record.Name, property);
    }

    [TestMethod]
    public void SettingRegistry_DuplicatePath_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(CaseDuplicateOptions)));

        refusal.Message.Should().ContainAll(nameof(CaseDuplicateOptions), "Value", "VALUE");
    }

    [TestMethod]
    public void SettingRegistry_SameRootTwice_Throws()
    {
        InvalidOperationException refusal = Refusal(
            Naming,
            new SettingRoot("Mailer", typeof(MailerOptions)),
            new SettingRoot("mailer", typeof(MailerOptions)));

        refusal.Message.Should().ContainAll(nameof(MailerOptions), "Host");
    }

    [TestMethod]
    public void SettingRegistry_TemplateOverlapsConcretePath_Throws()
    {
        InvalidOperationException refusal = Refusal(
            Naming,
            new SettingRoot("Feeds:*", typeof(FeedOptions)),
            new SettingRoot("Feeds:News", typeof(FeedOptions)));

        refusal.Message.Should().ContainAll(nameof(FeedOptions), "Url", "Feeds:*:Url", "Feeds:News:Url");
    }

    [TestMethod]
    public void SettingRegistry_TwoWildcards_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Feeds:*:Items:*", typeof(FeedOptions)));

        refusal.Message.Should().ContainAll("Feeds:*:Items:*", nameof(FeedOptions));
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("Mail er", DisplayName = "space")]
    [DataRow("Mail_er", DisplayName = "underscore")]
    [DataRow("Mailer:", DisplayName = "trailing colon")]
    [DataRow(":Mailer", DisplayName = "leading colon")]
    [DataRow("Mailer::Retry", DisplayName = "empty segment")]
    [DataRow("Mail*", DisplayName = "wildcard inside a segment")]
    [DataRow("Mailer__Retry", DisplayName = "environment separator")]
    public void SettingRegistry_InvalidRootPath_Throws(string path)
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot(path, typeof(MailerOptions)));

        refusal.Message.Should().ContainAll($"'{path}'", nameof(MailerOptions));
    }

    [TestMethod]
    public void SettingRegistry_MissingRecordType_Throws()
    {
        Type missing = (new Type[1])[0];

        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Mailer", missing));

        refusal.Message.Should().Contain("'Mailer'");
    }

    [TestMethod]
    [DataRow("$APP__", "$APP__", "$LOCAL__", DisplayName = "equal")]
    [DataRow("$APP__", "$app__", "$LOCAL__", DisplayName = "equal ignoring case")]
    [DataRow("$APP__", "$APP__USER__", "$LOCAL__", DisplayName = "user prefix starts with the global one")]
    [DataRow("$APP__", "$USER__", "$USER", DisplayName = "user prefix starts with the local one")]
    public void SettingRegistry_OverlappingPrefixes_Throws(string global, string user, string local)
    {
        InvalidOperationException refusal = Refusal(new SettingNaming(global, user, local));

        refusal.Message.Should().Contain("prefix");
    }

    [TestMethod]
    [DataRow("", "$USER__", "$LOCAL__", nameof(SettingNaming.GlobalPrefix))]
    [DataRow("$APP__", " ", "$LOCAL__", nameof(SettingNaming.UserPrefix))]
    [DataRow("$APP__", "$USER__", "\t", nameof(SettingNaming.LocalPrefix))]
    public void SettingRegistry_BlankPrefix_Throws(string global, string user, string local, string member)
    {
        InvalidOperationException refusal = Refusal(new SettingNaming(global, user, local));

        refusal.Message.Should().Contain(member);
    }

    [TestMethod]
    public void SettingRegistry_BlankDescription_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(BlankDescriptionOptions)));

        refusal.Message.Should().ContainAll(nameof(BlankDescriptionOptions), "Value", "description");
    }

    [TestMethod]
    public void SettingRegistry_UndefinedScope_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(UndefinedScopeOptions)));

        refusal.Message.Should().ContainAll(nameof(UndefinedScopeOptions), "Value", "scope");
    }

    [TestMethod]
    public void SettingRegistry_InvalidPropertySegment_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(InvalidSegmentOptions)));

        refusal.Message.Should().ContainAll(nameof(InvalidSegmentOptions), "Max_Value");
    }

    [TestMethod]
    [DataRow(typeof(NonPublicSettingOptions), "Hidden", DisplayName = "internal")]
    [DataRow(typeof(StaticSettingOptions), "Shared", DisplayName = "static")]
    [DataRow(typeof(IndexerSettingOptions), "Item", DisplayName = "indexer")]
    [DataRow(typeof(WriteOnlySettingOptions), "Hidden", DisplayName = "write-only")]
    public void SettingRegistry_NotAPublicReadableInstanceProperty_Throws(Type record, string property)
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", record));

        refusal.Message.Should().ContainAll(record.Name, property);
    }

    [TestMethod]
    public void SettingRegistry_RootWithoutSettings_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(NoSettingsOptions)));

        refusal.Message.Should().ContainAll("'Sample'", nameof(NoSettingsOptions));
    }

    [TestMethod]
    public void SettingRegistry_RecursiveRecordGraph_Throws()
    {
        InvalidOperationException refusal = Refusal(Naming, new SettingRoot("Sample", typeof(RecursiveOptions)));

        refusal.Message.Should().ContainAll(nameof(RecursiveOptions), "Child");
    }

    [TestMethod]
    public void SettingRegistry_ScopePrefix_SelectsScope()
    {
        ISettingRegistry registry = SampleRegistry();

        registry.Find("$user__greeting__repeat").Should().BeEquivalentTo(new
        {
            Name = "$USER__Greeting__Repeat",
            Scope = SettingScope.User,
            DefaultValue = "1",
        });
        registry.Find("$APP__Greeting__Repeat").Should().BeNull();
        registry.Find("$LOCAL__Greeting__Repeat").Should().BeNull();
        registry.Find("$LOCAL__Greeting__Theme").Should().BeEquivalentTo(new
        {
            Name = "$LOCAL__Greeting__Theme",
            Scope = SettingScope.Local,
        });
    }

    [TestMethod]
    [DataRow("$APP__Greeting:Text", DisplayName = "configuration separator")]
    [DataRow("$APP__Mailer____Port", DisplayName = "empty middle segment")]
    [DataRow("$APP____Mailer__Port", DisplayName = "empty first segment")]
    [DataRow("$APP__Mailer__Port__", DisplayName = "empty last segment")]
    [DataRow("$APP__", DisplayName = "prefix only")]
    [DataRow("$APP__Mailer___Port", DisplayName = "three underscores")]
    public void SettingRegistry_NameWithColonOrEmptySegment_NotFound(string name)
        => SampleRegistry().Find(name).Should().BeNull();

    [TestMethod]
    public void SettingRegistry_FindNull_ThrowsArgumentNull()
    {
        ISettingRegistry registry = SampleRegistry();
        string missing = (new string[1])[0];

        Action find = () => registry.Find(missing);

        find.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    [TestMethod]
    public void SettingRegistry_All_RootOrderThenDeclarationOrder()
    {
        SampleRegistry().All.Select(descriptor => descriptor.Name).Should().Equal(
            "$APP__Greeting__Text",
            "$APP__Greeting__Tone",
            "$USER__Greeting__Repeat",
            "$LOCAL__Greeting__Theme",
            "$APP__Mailer__Host",
            "$APP__Mailer__Port",
            "$APP__Mailer__Password",
            "$APP__Mailer__Retry__Attempts",
            "$APP__Mailer__Retry__Delay",
            "$APP__Mailer__Backoff",
            "$APP__Mailer__Enabled",
            "$APP__Mailer__Timeout",
            "$APP__Mailer__Recipients",
            "$APP__Mailer__Relay",
            "$APP__Mailer__MaxBytes",
            "$APP__Mailer__Sender",
            "$APP__Feeds__*__Url",
            "$APP__Feeds__*__Limit",
            "$APP__Feeds__*__Proxy");
    }

    [TestMethod]
    public void SettingRegistry_All_IsReadOnly()
    {
        IReadOnlyList<SettingDescriptor> all = SampleRegistry().All;

        all.Should().NotBeAssignableTo<SettingDescriptor[]>();
        all.Should().BeAssignableTo<ICollection<SettingDescriptor>>()
            .Which.IsReadOnly.Should().BeTrue();
    }

    [TestMethod]
    public void SettingRegistry_NoRoots_IsEmpty()
    {
        ISettingRegistry registry = Registry(Naming);

        registry.All.Should().BeEmpty();
        registry.Find("$APP__Greeting__Text").Should().BeNull();
    }

    [TestMethod]
    public void SettingRegistry_NameOf_JoinsPrefixAndPath()
    {
        ISettingRegistry registry = SampleRegistry();

        registry.NameOf(SettingScope.Global, "Mailer:Retry:Attempts").Should().Be("$APP__Mailer__Retry__Attempts");
        registry.NameOf(SettingScope.User, "Greeting:Repeat").Should().Be("$USER__Greeting__Repeat");
        registry.NameOf(SettingScope.Local, "Greeting:Theme").Should().Be("$LOCAL__Greeting__Theme");
        registry.NameOf(SettingScope.Global, "Feeds:*:Url").Should().Be("$APP__Feeds__*__Url");
    }

    [TestMethod]
    public void SettingRegistry_NameOf_InvalidArguments_Throw()
    {
        ISettingRegistry registry = SampleRegistry();
        string missing = (new string[1])[0];

        Action undefinedScope = () => registry.NameOf((SettingScope)0, "Mailer:Port");
        Action withoutPath = () => registry.NameOf(SettingScope.Global, missing);

        undefinedScope.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("scope");
        withoutPath.Should().Throw<ArgumentNullException>().WithParameterName("path");
    }

    [TestMethod]
    public void SettingRegistry_ConcurrentReaders_SeeTheSameAnswers()
    {
        ISettingRegistry registry = SampleRegistry();
        SettingDescriptor template = registry.All.Single(descriptor => descriptor.Path == "Feeds:*:Url");

        string[] paths = new string[64];
        Parallel.For(0, paths.Length, index =>
        {
            SettingDescriptor? found = registry.Find($"$APP__Feeds__Feed{index}__Url");
            SettingDescriptor made = registry.Instantiate(template, $"Feed{index}");
            paths[index] = found is not null && found == made ? found.Path : "mismatch";
        });

        paths.Should().Equal(Enumerable.Range(0, paths.Length).Select(index => $"Feeds:Feed{index}:Url"));
    }

    [TestMethod]
    [DataRow("A:*:B", true)]
    [DataRow("*", true)]
    [DataRow("*:B", true)]
    [DataRow("A:*", true)]
    [DataRow("A:B", false)]
    [DataRow("A*:B", false)]
    [DataRow("A:B*", false)]
    public void SettingDescriptor_IsTemplate_TrueOnlyForAWildcardSegment(string path, bool expected)
    {
        var descriptor = new SettingDescriptor(
            "name", path, SettingScope.Global, typeof(string), "A value.", false, false, true, null);

        descriptor.IsTemplate.Should().Be(expected);
    }

    private static string? DefaultOf(ISettingRegistry registry, string path)
        => registry.All.Single(descriptor => descriptor.Path == path).DefaultValue;

    private static SettingRoot[] SampleRoots() =>
    [
        new("Greeting", typeof(GreetingOptions)),
        new("Mailer", typeof(MailerOptions), ["Sender", "retry:attempts"]),
        new("Feeds:*", typeof(FeedOptions), ["Limit"]),
    ];

    private ISettingRegistry SampleRegistry() => Registry(Naming, SampleRoots());

    private ISettingRegistry Registry(SettingNaming naming, params SettingRoot[] roots)
        => Services(naming, roots).GetRequiredService<ISettingRegistry>();

    private IServiceProvider Services(SettingNaming naming, SettingRoot[] roots)
        => GetServices(builder =>
        {
            builder.RegisterInstance(naming);
            foreach (SettingRoot root in roots)
            {
                builder.RegisterInstance(root);
            }
        });

    // Resolved through Autofac itself: the IServiceProvider facade wraps the resolution failure in
    // one more InvalidOperationException that carries Autofac's text, not the registry's.
    private InvalidOperationException Refusal(SettingNaming naming, params SettingRoot[] roots)
    {
        ILifetimeScope scope = Services(naming, roots).GetRequiredService<ILifetimeScope>();
        Func<ISettingRegistry> build = () => scope.Resolve<ISettingRegistry>();

        Exception cause = build.Should().Throw<DependencyResolutionException>().Which.UnwrapResolutionFailures();

        return cause.Should().BeOfType<InvalidOperationException>().Subject;
    }
}
