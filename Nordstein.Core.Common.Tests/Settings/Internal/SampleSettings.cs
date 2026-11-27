using Nordstein.Core.Common.Settings;

namespace Nordstein.Core.Common.Tests.Settings.Internal;

// Neutral sample configuration records for SettingRegistryTests: valid shapes first, then one
// record per construction failure the registry must refuse.

internal enum GreetingTone
{
    Plain = 1,
    Warm = 2,
}

internal abstract record GreetingBase
{
    [Setting(SettingScope.Global, "The greeting's text.")]
    public string Text { get; init; } = "Hello";
}

internal sealed record GreetingOptions : GreetingBase
{
    [Setting(SettingScope.Global, "The greeting's tone.")]
    public GreetingTone Tone { get; init; } = GreetingTone.Warm;

    [Setting(SettingScope.User, "How often the greeting repeats for this user.")]
    public int Repeat { get; init; } = 1;

    [Setting(SettingScope.Local, "The greeting's colour theme in this browser.")]
    public string Theme { get; init; } = "light";

    public string Unmarked { get; init; } = "ignored";
}

internal sealed record MailerOptions
{
    [Setting(SettingScope.Global, "The mail server's host name.")]
    [Sink]
    public string Host { get; init; } = "localhost";

    [Setting(SettingScope.Global, "The mail server's port.")]
    public int Port { get; init; } = 25;

    [Setting(SettingScope.Global, "The mail server's password.")]
    [Protected]
    [Sink]
    public string? Password { get; init; }

    public MailerRetry Retry { get; init; } = new();

    [Setting(SettingScope.Global, "The backoff factor between retries.")]
    public double Backoff { get; init; } = 2.5;

    [Setting(SettingScope.Global, "Whether mail is sent at all.")]
    public bool Enabled { get; init; } = true;

    [Setting(SettingScope.Global, "How long one delivery may take.")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);

    [Setting(SettingScope.Global, "The recipient patterns mail may go to.")]
    public IReadOnlyList<string> Recipients { get; init; } = ["*"];

    [Setting(SettingScope.Global, "The relay every message passes through.")]
    public Uri Relay { get; init; } = new("https://relay.example/v1");

    [Setting(SettingScope.Global, "The largest message in bytes.")]
    public long MaxBytes { get; init; } = 10_485_760;

    [Setting(SettingScope.Global, "The sender address.")]
    public required string Sender { get; init; }
}

internal sealed record MailerRetry
{
    [Setting(SettingScope.Global, "How often a delivery is attempted.")]
    public int Attempts { get; init; } = 3;

    [Setting(SettingScope.Global, "The wait before the first retry.")]
    public TimeSpan? Delay { get; init; }
}

internal sealed record FeedOptions
{
    [Setting(SettingScope.Global, "The feed's address.")]
    [Sink]
    public required Uri Url { get; init; }

    [Setting(SettingScope.Global, "The most items read per poll.")]
    public int Limit { get; init; } = 20;

    [Setting(SettingScope.Global, "The proxy the feed is read through, by name.", ValueType = typeof(string))]
    public ProxyOptions Proxy { get; init; } = new();
}

internal sealed record ProxyOptions
{
    [Setting(SettingScope.Global, "The proxy's name.")]
    public string Name { get; init; } = "direct";
}

internal sealed record SizedOptions([property: Setting(SettingScope.Global, "The size in items.")] int Size);

internal sealed record FormatOptions
{
    [Setting(SettingScope.Global, "A ratio that is not a number.")]
    public double Ratio { get; init; } = double.NaN;

    [Setting(SettingScope.Global, "A negative offset.")]
    public int Offset { get; init; } = -3;

    [Setting(SettingScope.Global, "A large negative count.")]
    public long Big { get; init; } = -1_234_567_890_123;

    [Setting(SettingScope.Global, "A negative grace period.")]
    public TimeSpan? Grace { get; init; } = new TimeSpan(-1, -2, -3, -4);

    [Setting(SettingScope.Global, "A nullable limit that is set.")]
    public int? Limit { get; init; } = 7;

    [Setting(SettingScope.Global, "Patterns that need escaping.")]
    public IReadOnlyList<string?> Patterns { get; init; } = ["a+b", "<x>", "é", null];

    [Setting(SettingScope.Global, "A tone, by name.", ValueType = typeof(GreetingTone))]
    public string ToneName { get; init; } = "Warm";

    [Setting(SettingScope.Global, "An access token.")]
    [Protected]
    public string Token { get; init; } = "changeme";
}

internal readonly record struct WindowOptions
{
    public WindowOptions()
    {
    }

    [Setting(SettingScope.Global, "The window's width in columns.")]
    public int Width { get; init; } = 80;
}

internal sealed record ScreenOptions
{
    public WindowOptions? Window { get; init; } = new WindowOptions();

    public WindowOptions? Spare { get; init; }
}

internal sealed record OuterOptions
{
    public MiddleGroup Middle { get; init; } = new();
}

internal sealed record MiddleGroup
{
    public MailerRetry Retry { get; init; } = new();
}

internal sealed record HiddenSectionOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    public HiddenOnly Inner { get; init; } = new();
}

internal sealed record HiddenOnly
{
    [Setting(SettingScope.Global, "A value no binder can reach.")]
    internal string Secret { get; init; } = "";
}

internal abstract record KeyBase
{
    [Setting(SettingScope.Global, "The access key.")]
    public virtual string Key { get; init; } = "none";
}

internal sealed record KeyOptions : KeyBase
{
    [Protected]
    [Sink]
    public override string Key { get; init; } = "secret";
}

internal abstract record CodeBase
{
    [Setting(SettingScope.Global, "The code as text.")]
    public string Code { get; init; } = "a";
}

internal sealed record CodeOptions : CodeBase
{
    [Setting(SettingScope.Global, "The code as a number.")]
    public new int Code { get; init; } = 1;
}

internal sealed record RetryCountOptions
{
    [Setting(SettingScope.Global, "How often to retry.")]
    public int Retry { get; init; } = 1;
}

internal sealed record ThrowingConstructorOptions
{
    public ThrowingConstructorOptions()
    {
        throw new NotSupportedException("The sample constructor refuses.");
    }

    [Setting(SettingScope.Global, "A value.")]
    public string Value { get; init; } = "";
}

internal sealed record ThrowingGetterOptions
{
    [Setting(SettingScope.Global, "A value whose getter refuses.")]
    public string Value => throw new NotSupportedException("The sample getter refuses.");
}

internal sealed record UnsupportedDecimalOptions
{
    [Setting(SettingScope.Global, "A price.")]
    public decimal Price { get; init; }
}

internal sealed record UnsupportedArrayOptions
{
    [Setting(SettingScope.Global, "Some names.")]
    public string[] Names { get; init; } = [];
}

internal sealed record UnsupportedListOptions
{
    [Setting(SettingScope.Global, "Some numbers.")]
    public IReadOnlyList<int> Numbers { get; init; } = [];
}

internal sealed record UnsupportedValueTypeOptions
{
    [Setting(SettingScope.Global, "A moment.", ValueType = typeof(DateTimeOffset))]
    public string Moment { get; init; } = "";
}

internal sealed record ProtectedWithoutSettingOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    [Protected]
    public string Secret { get; init; } = "";
}

internal sealed record SinkWithoutSettingOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    [Sink]
    public string Target { get; init; } = "";
}

internal sealed record CaseDuplicateOptions
{
    [Setting(SettingScope.Global, "A value.")]
    public string Value { get; init; } = "";

    [Setting(SettingScope.Global, "The same value, shouted.")]
    public string VALUE { get; init; } = "";
}

internal sealed record BlankDescriptionOptions
{
    [Setting(SettingScope.Global, " ")]
    public string Value { get; init; } = "";
}

internal sealed record UndefinedScopeOptions
{
    [Setting((SettingScope)7, "A value in no scope.")]
    public string Value { get; init; } = "";
}

internal sealed record InvalidSegmentOptions
{
    [Setting(SettingScope.Global, "A value with an underscore in its name.")]
    public string Max_Value { get; init; } = "";
}

internal sealed record NonPublicSettingOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    [Setting(SettingScope.Global, "A hidden value.")]
    internal string Hidden { get; init; } = "";
}

internal sealed record StaticSettingOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    [Setting(SettingScope.Global, "A value shared by every instance.")]
    public static string Shared => "shared";
}

internal sealed record IndexerSettingOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    [Setting(SettingScope.Global, "A value by position.")]
    public string this[int position] => Visible;
}

internal sealed record WriteOnlySettingOptions
{
    [Setting(SettingScope.Global, "A visible value.")]
    public string Visible { get; init; } = "";

    [Setting(SettingScope.Global, "A value that cannot be read back.")]
    public string Hidden
    {
        init => Visible = value;
    }
}

internal abstract record LabelBase
{
    [Setting(SettingScope.Global, "The label's text.")]
    public virtual string Label { get; init; } = "base";
}

internal sealed record LabelOptions : LabelBase
{
    [Setting(SettingScope.Global, "The label's size in points.")]
    public int Size { get; init; } = 12;

    public override string Label { get; init; } = "derived";
}

internal sealed record NoSettingsOptions
{
    public string Text { get; init; } = "";
}

internal sealed record RecursiveOptions
{
    [Setting(SettingScope.Global, "The depth.")]
    public int Depth { get; init; }

    public RecursiveOptions? Child { get; init; }
}
