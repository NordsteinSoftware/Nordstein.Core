using System.ComponentModel.DataAnnotations;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class PasswordPolicyTests : BaseTest<Module>
{
    private const string Email = "jordan.doe@example.com";
    private const string DisplayName = "Jordan Doe-Smith";

    [TestMethod]
    public void Check_ElevenCodePoints_IsTooShort()
        => RuleOf(Check(new string('a', 11))).Should().Be(PasswordRule.TooShort);

    [TestMethod]
    public void Check_TwelveCodePoints_IsAccepted()
        => Check(new string('a', 12)).Should().BeNull();

    [TestMethod]
    public void Check_TwelveAstralCodePoints_CountsCodePointsNotUtf16Units()
    {
        string twelveEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 12));
        string elevenEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 11));

        twelveEmoji.Length.Should().Be(24);
        Check(twelveEmoji).Should().BeNull();
        RuleOf(Check(elevenEmoji)).Should().Be(PasswordRule.TooShort);
    }

    [TestMethod]
    public void Check_256CodePoints_IsAccepted()
        => Check(new string('a', 256)).Should().BeNull();

    [TestMethod]
    public void Check_257CodePoints_IsTooLong()
        => RuleOf(Check(new string('a', 257))).Should().Be(PasswordRule.TooLong);

    [TestMethod]
    public void Check_257AstralCodePoints_IsTooLong()
        => RuleOf(Check(string.Concat(Enumerable.Repeat("\U0001F600", 257)))).Should().Be(PasswordRule.TooLong);

    [TestMethod]
    public void Check_NfkcFoldsBeforeCounting()
    {
        // U+FB01 is one code point that NFKC folds into the two letters "fi".
        string ligature = "ﬁ" + new string('a', 10);

        ligature.EnumerateRunes().Count().Should().Be(11);
        Check(ligature).Should().BeNull();
    }

    [TestMethod]
    public void Normalize_FullwidthDigits_FoldToAscii()
        => Policy().Normalize("１２３４５６７８９０１２").Should().Be("123456789012");

    [TestMethod]
    public void Normalize_SpacesAndCase_AreKept()
        => Policy().Normalize(" Pass Word 123 ").Should().Be(" Pass Word 123 ");

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Normalize_UnpairedSurrogate_ThrowsWithoutThePassword(int sample)
    {
        // Attribute arguments are stored as UTF-8, which cannot carry an unpaired surrogate: build them here.
        string password = sample switch
        {
            0 => "secret\uD800password",
            1 => "\uDC00secret-password",
            _ => "secret-password\uD83D",
        };
        IPasswordPolicy policy = Policy();

        Action act = () => policy.Normalize(password);

        ArgumentException thrown = act.Should().Throw<ArgumentException>().Which;
        thrown.ParamName.Should().Be("password");
        thrown.Message.Should().NotContain("secret").And.NotContain("password\uD83D");
    }

    [TestMethod]
    public void Normalize_Null_ThrowsArgumentNull()
    {
        IPasswordPolicy policy = Policy();
        string missing = (new string[1])[0];

        Action act = () => policy.Normalize(missing);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("password");
    }

    [TestMethod]
    public void Check_NoCompositionRules_AllLowercaseLettersAccepted()
        => Check("abcdefghijkl").Should().BeNull();

    [TestMethod]
    public void Check_EqualsAnIdentity_IsRefused()
    {
        RuleOf(Check(Email)).Should().Be(PasswordRule.MatchesIdentity);
        RuleOf(Check(DisplayName)).Should().Be(PasswordRule.MatchesIdentity);
    }

    [TestMethod]
    public void Check_EqualsAnIdentityInAnotherCase_IsRefused()
    {
        RuleOf(Check("JORDAN.DOE@EXAMPLE.COM")).Should().Be(PasswordRule.MatchesIdentity);
        RuleOf(Check("jORDAN dOE-sMITH")).Should().Be(PasswordRule.MatchesIdentity);
    }

    [TestMethod]
    public void Check_EqualsAnIdentityOnlyAfterNfkc_IsRefused()
    {
        // The identity is stored with a ligature; the password spells it out.
        RuleOf(Check("Profile-Admin-1", identities: ["Proﬁle-Admin-1"])).Should().Be(PasswordRule.MatchesIdentity);
    }

    [TestMethod]
    public void Check_ContainsAnIdentity_IsAccepted()
    {
        Check(Email + "-and-more").Should().BeNull();
        Check("my " + DisplayName + " 2026").Should().BeNull();
    }

    [TestMethod]
    public void Check_NoIdentities_SkipsTheRule()
        => Check(Email, identities: []).Should().BeNull();

    [TestMethod]
    public void Check_IdentitiesAsALazySequence_AreEnumeratedOnce()
    {
        IPasswordPolicy policy = Policy();
        int enumerations = 0;
        IEnumerable<string> Identities()
        {
            enumerations++;
            yield return Email;
            yield return DisplayName;
        }

        policy.Check(policy.Normalize("correct horse battery"), Identities());

        enumerations.Should().Be(1);
    }

    [TestMethod]
    public void Check_TooShortAndAnIdentity_ReportsTooShortFirst()
        => RuleOf(Check("short", identities: ["short"])).Should().Be(PasswordRule.TooShort);

    [TestMethod]
    public void Check_TooLongAndAnIdentity_ReportsTooLongFirst()
    {
        string tooLong = new('a', 257);

        RuleOf(Check(tooLong, identities: [tooLong])).Should().Be(PasswordRule.TooLong);
    }

    [TestMethod]
    public void Check_NotNormalized_ThrowsWithoutThePassword()
    {
        IPasswordPolicy policy = Policy();

        Action act = () => policy.Check("ﬁ-secret-password", [Email]);

        ArgumentException thrown = act.Should().Throw<ArgumentException>().Which;
        thrown.ParamName.Should().Be("normalized");
        thrown.Message.Should().NotContain("secret-password");
    }

    [TestMethod]
    public void Check_UnpairedSurrogateInPassword_ThrowsWithoutThePassword()
    {
        IPasswordPolicy policy = Policy();

        Action act = () => policy.Check("secret-password\uD800", [Email]);

        ArgumentException thrown = act.Should().Throw<ArgumentException>().Which;
        thrown.ParamName.Should().Be("normalized");
        thrown.Message.Should().NotContain("secret-password");
    }

    [TestMethod]
    public void Check_NullIdentity_ThrowsArgument()
    {
        IPasswordPolicy policy = Policy();
        string missing = (new string[1])[0];

        Action act = () => policy.Check("correct horse battery", [Email, missing]);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("identities");
    }

    [TestMethod]
    public void Check_IdentityWithAnUnpairedSurrogate_ThrowsWithoutTheIdentity()
    {
        IPasswordPolicy policy = Policy();

        Action act = () => policy.Check("correct horse battery", ["private-name\uDC00"]);

        ArgumentException thrown = act.Should().Throw<ArgumentException>().Which;
        thrown.ParamName.Should().Be("identities");
        thrown.Message.Should().NotContain("private-name");
    }

    [TestMethod]
    public void Check_NullArguments_ThrowArgumentNull()
    {
        IPasswordPolicy policy = Policy();
        string missing = (new string[1])[0];
        IEnumerable<string> noIdentities = (new IEnumerable<string>[1])[0];

        Action password = () => policy.Check(missing, [Email]);
        Action identities = () => policy.Check("correct horse battery", noIdentities);

        password.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("normalized");
        identities.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("identities");
    }

    [TestMethod]
    public void Check_Refusals_HoldNeitherPasswordNorIdentity()
    {
        string tooShort = "secret-pw-1";   // 11 code points
        string tooLong = new string('x', 257) + "TooLongMarker";
        PasswordPolicyException[] refusals =
        [
            Check(tooShort) ?? throw new InvalidOperationException("expected TooShort"),
            Check(tooLong) ?? throw new InvalidOperationException("expected TooLong"),
            Check(Email) ?? throw new InvalidOperationException("expected MatchesIdentity"),
        ];

        refusals.Select(e => e.Rule).Should().Equal(PasswordRule.TooShort, PasswordRule.TooLong, PasswordRule.MatchesIdentity);
        foreach (PasswordPolicyException refusal in refusals)
        {
            refusal.Message.Should().NotBeNullOrWhiteSpace()
                .And.NotContain(tooShort).And.NotContain("TooLongMarker").And.NotContain(Email).And.NotContain("jordan");
            refusal.ToString().Should().NotContain(tooShort).And.NotContain(Email);
        }

        refusals.Select(e => e.Message).Distinct().Should().HaveCount(3);
    }

    [TestMethod]
    public void Rules_HaveTheirStableNumbers()
    {
        ((int)PasswordRule.TooShort).Should().Be(1);
        ((int)PasswordRule.TooLong).Should().Be(2);
        ((int)PasswordRule.MatchesIdentity).Should().Be(3);
    }

    [TestMethod]
    public void Check_MinimumZero_EmptyPasswordAccepted()
        => Check(string.Empty, minLength: 0).Should().BeNull();

    [TestMethod]
    public void Check_MinimumZero_StillRefusesTheIdentity()
        => RuleOf(Check("JORDAN DOE-SMITH", minLength: 0)).Should().Be(PasswordRule.MatchesIdentity);

    [TestMethod]
    public void Check_MinimumZero_StillRefusesTooLong()
        => RuleOf(Check(new string('a', 257), minLength: 0)).Should().Be(PasswordRule.TooLong);

    [TestMethod]
    public void Check_MinimumFive_TheBoundaryFollowsTheOption()
    {
        RuleOf(Check(new string('a', 4), minLength: 5)).Should().Be(PasswordRule.TooShort);
        Check(new string('a', 5), minLength: 5).Should().BeNull();
    }

    [TestMethod]
    public void Check_Minimum256_OnlyTheLongestPasswordPasses()
    {
        RuleOf(Check(new string('a', 255), minLength: 256)).Should().Be(PasswordRule.TooShort);
        Check(new string('a', 256), minLength: 256).Should().BeNull();
    }

    [TestMethod]
    public void Check_TooShortUnderMinimumSixteen_MessageNamesSixteen()
    {
        PasswordPolicyException refused = Check(new string('a', 13), minLength: 16)
            ?? throw new InvalidOperationException("expected the policy to refuse the password");

        refused.Rule.Should().Be(PasswordRule.TooShort);
        refused.MinimumLength.Should().Be(16);
        refused.Message.Should().Be("The password must have at least 16 characters.");
    }

    [TestMethod]
    public void Check_DefaultMinimum_EmptyPasswordIsTooShort()
        => RuleOf(Check(string.Empty)).Should().Be(PasswordRule.TooShort);

    [TestMethod]
    [DataRow(-1)]
    [DataRow(257)]
    [DataRow(int.MaxValue)]
    public void Resolve_MinimumOutsideTheRange_FailsWithValidation(int minLength)
    {
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(new PasswordPolicyOptions { MinLength = minLength }));

        Action act = () => services.GetRequiredService<IPasswordPolicy>();

        CauseOf(act.Should().Throw<InvalidOperationException>().Which)
            .Should().BeOfType<ValidationException>().Which.ValidationResult.MemberNames
            .Should().Equal(nameof(PasswordPolicyOptions.MinLength));
    }

    [TestMethod]
    public void Options_Defaults_AreTwelveTo256()
    {
        new PasswordPolicyOptions().MinLength.Should().Be(12);
        PasswordPolicyOptions.DefaultMinLength.Should().Be(12);
        PasswordPolicyOptions.MaxLength.Should().Be(256);
    }

    [TestMethod]
    public void Container_ResolvesOneInstanceWithTheDefaultOptions()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<IPasswordPolicy>().Should().BeSameAs(services.GetRequiredService<IPasswordPolicy>());
        services.GetRequiredService<PasswordPolicyOptions>().Should().Be(new PasswordPolicyOptions());
    }

    [TestMethod]
    public void Exception_EachRule_HasAFixedMessage()
    {
        PasswordRule[] rules = [PasswordRule.TooShort, PasswordRule.TooLong, PasswordRule.MatchesIdentity];

        string[] messages = [.. rules.Select(rule => new PasswordPolicyException(rule, 12).Message)];

        messages.Should().OnlyHaveUniqueItems().And.AllSatisfy(message => message.Should().NotBeNullOrWhiteSpace());
        rules.Select(rule => new PasswordPolicyException(rule, 12).Rule).Should().Equal(rules);
    }

    [TestMethod]
    public void Exception_UndefinedRule_Throws()
    {
        Action act = () => _ = new PasswordPolicyException((PasswordRule)99, 12);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("rule");
    }

    [TestMethod]
    public void Exception_TooShortUnderMinimumOne_SaysOneCharacter()
        => new PasswordPolicyException(PasswordRule.TooShort, 1).Message.Should().Be("The password must have at least 1 character.");

    [TestMethod]
    public void Exception_OnlyTooShortNamesTheMinimum()
    {
        new PasswordPolicyException(PasswordRule.TooLong, 16).Message.Should().Be("The password must have at most 256 characters.");
        new PasswordPolicyException(PasswordRule.MatchesIdentity, 16).Message.Should().NotContain("16");
        new PasswordPolicyException(PasswordRule.MatchesIdentity, 16).MinimumLength.Should().Be(16);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(257)]
    public void Exception_MinimumOutsideTheOptionRange_Throws(int minimumLength)
    {
        Action act = () => _ = new PasswordPolicyException(PasswordRule.TooShort, minimumLength);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("minimumLength");
    }

    private static PasswordRule RuleOf(PasswordPolicyException? exception)
        => (exception ?? throw new InvalidOperationException("expected the policy to refuse the password")).Rule;

    private IPasswordPolicy Policy(int? minLength = null)
        => GetServices(builder =>
            {
                if (minLength is { } min)
                {
                    builder.RegisterInstance(new PasswordPolicyOptions { MinLength = min });
                }
            })
            .GetRequiredService<IPasswordPolicy>();

    /// <summary>
    /// The exception the policy throws for the password, or <see langword="null"/> when it accepts it.
    /// </summary>
    private PasswordPolicyException? Check(string password, int? minLength = null, string[]? identities = null)
    {
        IPasswordPolicy policy = Policy(minLength);
        try
        {
            policy.Check(policy.Normalize(password), identities ?? [Email, DisplayName]);
            return null;
        }
        catch (PasswordPolicyException exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// The cause of a failed <c>GetRequiredService</c>: Microsoft's service-provider contract wraps Autofac's
    /// resolution failure in an <see cref="InvalidOperationException"/>.
    /// </summary>
    private static Exception CauseOf(InvalidOperationException thrown)
        => (thrown.InnerException ?? thrown).UnwrapResolutionFailures();
}
