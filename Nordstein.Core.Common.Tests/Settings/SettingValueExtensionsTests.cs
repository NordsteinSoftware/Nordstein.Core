using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Settings;
using Nordstein.Core.Common.Tests.Settings.Internal;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Settings;

/// <summary>
/// A setting's value kind and the check of a text typed for it: invariant parsing per type, a failure
/// that names the path and the form but never the value, and every registry default accepted.
/// </summary>
[TestClass]
public sealed class SettingValueExtensionsTests : BaseTest<Module>
{
    private const string Path = "Section:Value";

    [TestMethod]
    [DataRow(typeof(string), SettingValueKind.Text)]
    [DataRow(typeof(GreetingTone), SettingValueKind.Choice)]
    [DataRow(typeof(bool), SettingValueKind.Boolean)]
    [DataRow(typeof(int), SettingValueKind.Integer)]
    [DataRow(typeof(long), SettingValueKind.Integer)]
    [DataRow(typeof(double), SettingValueKind.Number)]
    [DataRow(typeof(TimeSpan), SettingValueKind.Duration)]
    [DataRow(typeof(Uri), SettingValueKind.Url)]
    [DataRow(typeof(IReadOnlyList<string>), SettingValueKind.List)]
    public void ValueKind_EverySupportedType_HasItsKind(Type type, SettingValueKind expected)
    {
        Descriptor(type).ValueKind().Should().Be(expected);
    }

    [TestMethod]
    [DataRow(typeof(string), "")]
    [DataRow(typeof(string), "anything \u0000 at all")]
    [DataRow(typeof(bool), "true")]
    [DataRow(typeof(bool), "False")]
    [DataRow(typeof(bool), " TRUE ")]
    [DataRow(typeof(int), "-2147483648")]
    [DataRow(typeof(int), " +42 ")]
    [DataRow(typeof(long), "9223372036854775807")]
    [DataRow(typeof(double), "2.5")]
    [DataRow(typeof(double), "-1e-3")]
    [DataRow(typeof(double), "NaN")]
    [DataRow(typeof(double), "-Infinity")]
    [DataRow(typeof(TimeSpan), "00:01:30")]
    [DataRow(typeof(TimeSpan), "1.02:03:04.5")]
    [DataRow(typeof(TimeSpan), "5")]
    [DataRow(typeof(Uri), "https://relay.example/v1?x=1")]
    [DataRow(typeof(Uri), "mailto:someone@example.org")]
    [DataRow(typeof(Uri), "FILE:///var/data")]
    [DataRow(typeof(GreetingTone), "Warm")]
    [DataRow(typeof(GreetingTone), "plain")]
    [DataRow(typeof(IReadOnlyList<string>), "[]")]
    [DataRow(typeof(IReadOnlyList<string>), "[\"*\", null, \"a\"]")]
    public void CheckValue_TextTheTypeHolds_Passes(Type type, string value)
    {
        Descriptor(type).CheckValue(value).Should().BeNull();
    }

    [TestMethod]
    [DataRow(typeof(bool), "yes", "true or false")]
    [DataRow(typeof(bool), "1", "true or false")]
    [DataRow(typeof(int), "2147483648", "an integer")]
    [DataRow(typeof(int), "1.0", "an integer")]
    [DataRow(typeof(int), "1,000", "an integer")]
    [DataRow(typeof(int), "0x10", "an integer")]
    [DataRow(typeof(int), "", "an integer")]
    [DataRow(typeof(long), "9223372036854775808", "an integer")]
    [DataRow(typeof(double), "2,5", "a number")]
    [DataRow(typeof(double), "1,000.5", "a number")]
    [DataRow(typeof(double), "two", "a number")]
    [DataRow(typeof(TimeSpan), "90 seconds", "a duration such as 00:01:30")]
    [DataRow(typeof(TimeSpan), "1h30m", "a duration such as 00:01:30")]
    [DataRow(typeof(Uri), "relay.example/v1", "an absolute URL")]
    [DataRow(typeof(Uri), "/var/data", "an absolute URL")]
    [DataRow(typeof(Uri), " https://relay.example", "an absolute URL")]
    [DataRow(typeof(Uri), "", "an absolute URL")]
    [DataRow(typeof(GreetingTone), "1", "one of Plain, Warm")]
    [DataRow(typeof(GreetingTone), "Plain, Warm", "one of Plain, Warm")]
    [DataRow(typeof(GreetingTone), " Warm", "one of Plain, Warm")]
    [DataRow(typeof(GreetingTone), "Loud", "one of Plain, Warm")]
    [DataRow(typeof(IReadOnlyList<string>), "*", "a JSON array of strings")]
    [DataRow(typeof(IReadOnlyList<string>), "[1]", "a JSON array of strings")]
    [DataRow(typeof(IReadOnlyList<string>), "[\"a\",]", "a JSON array of strings")]
    [DataRow(typeof(IReadOnlyList<string>), "{\"a\": \"b\"}", "a JSON array of strings")]
    [DataRow(typeof(IReadOnlyList<string>), "[[\"a\"]]", "a JSON array of strings")]
    public void CheckValue_TextTheTypeCannotHold_NamesThePathAndTheFormButNotTheValue(Type type, string value, string form)
    {
        ValidationResult? result = Descriptor(type).CheckValue(value);

        result.Should().NotBeNull();
        result?.ErrorMessage.Should().Be($"{Path} must be {form}.");
        result?.MemberNames.Should().Equal(Path);
        if (value.Trim().Length > 1 && !form.Contains(value.Trim(), StringComparison.Ordinal))
        {
            result?.ErrorMessage.Should().NotContain(value.Trim());
        }
    }

    [TestMethod]
    public void CheckValue_UnderAnotherCulture_StillReadsInvariantly()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");

            (Descriptor(typeof(double)).CheckValue("2.5"), Descriptor(typeof(double)).CheckValue("2,5") is null).Should().Be((null, false));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void CheckValue_EveryRegistryDefault_Passes()
    {
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(new SettingNaming("$APP__", "$USER__", "$LOCAL__"));
            builder.RegisterInstance(new SettingRoot("Greeting", typeof(GreetingOptions)));
            builder.RegisterInstance(new SettingRoot("Mailer", typeof(MailerOptions)));
            builder.RegisterInstance(new SettingRoot("Format", typeof(FormatOptions)));
            builder.RegisterInstance(new SettingRoot("Stations:*", typeof(StationOptions)));
        });
        SettingDescriptor[] withDefaults = [.. services.GetRequiredService<ISettingRegistry>().All.Where(descriptor => descriptor.DefaultValue is not null)];

        withDefaults.Should().HaveCountGreaterThan(10);
        withDefaults.Should().AllSatisfy(descriptor => descriptor.CheckValue(descriptor.DefaultValue ?? string.Empty).Should().BeNull(descriptor.Path));
        withDefaults.Select(descriptor => descriptor.ValueKind()).Distinct().Should().HaveCount(8, "the sample defaults cover every kind");
    }

    [TestMethod]
    public void UnsupportedType_Throws()
    {
        SettingDescriptor descriptor = Descriptor(typeof(decimal));

        FluentActions.Invoking(() => descriptor.ValueKind()).Should().Throw<InvalidOperationException>().WithMessage($"*{Path}*Decimal*");
        FluentActions.Invoking(() => descriptor.CheckValue("1")).Should().Throw<InvalidOperationException>().WithMessage($"*{Path}*Decimal*");
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
        // WHY: the null-forgiving operator is banned, tests included. A default array element is a
        // genuine null the compiler types as non-null, as a mis-wired caller would pass.
        SettingDescriptor missing = (new SettingDescriptor[1])[0];

        FluentActions.Invoking(() => missing.ValueKind()).Should().Throw<ArgumentNullException>().WithParameterName("descriptor");
        FluentActions.Invoking(() => missing.CheckValue("1")).Should().Throw<ArgumentNullException>().WithParameterName("descriptor");
        FluentActions.Invoking(() => Descriptor(typeof(int)).CheckValue((new string[1])[0])).Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    private static SettingDescriptor Descriptor(Type type)
        => new("$APP__Section__Value", Path, SettingScope.Global, type, "A value.", false, false, true, null);
}
