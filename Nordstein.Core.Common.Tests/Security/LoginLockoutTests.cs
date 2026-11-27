using System.ComponentModel.DataAnnotations;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests.Security;

[TestClass]
public sealed class LoginLockoutTests : BaseTest<Module>
{
    private static DateTimeOffset Now => new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(0, null)]
    [DataRow(1, null)]
    [DataRow(4, null)]
    [DataRow(5, 1)]
    [DataRow(6, 2)]
    [DataRow(7, 4)]
    [DataRow(8, 8)]
    [DataRow(9, 15)]
    [DataRow(10, 15)]
    [DataRow(int.MaxValue, 15)]
    public void LockFor_DefaultOptions_FollowsTheBackoff(int failures, int? expectedMinutes)
    {
        var lockout = GetServices().GetRequiredService<ILoginLockout>();

        TimeSpan? locked = lockout.LockFor(failures);

        locked.Should().Be(expectedMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void LockFor_NegativeCount_Throws(int failures)
    {
        var lockout = GetServices().GetRequiredService<ILoginLockout>();

        Action act = () => lockout.LockFor(failures);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("consecutiveFailures");
    }

    [TestMethod]
    [DataRow(0, null)]
    [DataRow(1, 10)]
    [DataRow(2, 20)]
    [DataRow(3, 25)]
    [DataRow(4, 25)]
    public void LockFor_CustomOptions_FollowsThem(int failures, int? expectedSeconds)
    {
        ILoginLockout lockout = Lockout(new LoginLockoutOptions
        {
            Threshold = 1,
            InitialLockDuration = TimeSpan.FromSeconds(10),
            MaxLockDuration = TimeSpan.FromSeconds(25),
        });

        lockout.LockFor(failures).Should().Be(expectedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
    }

    [TestMethod]
    public void LockFor_MaximumEqualsInitial_NeverDoubles()
    {
        ILoginLockout lockout = Lockout(new LoginLockoutOptions
        {
            Threshold = 3,
            InitialLockDuration = TimeSpan.FromMinutes(1),
            MaxLockDuration = TimeSpan.FromMinutes(1),
        });

        lockout.LockFor(3).Should().Be(TimeSpan.FromMinutes(1));
        lockout.LockFor(4).Should().Be(TimeSpan.FromMinutes(1));
        lockout.LockFor(int.MaxValue).Should().Be(TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public void LockFor_OneTickDoublingTowardsTheLargestSpan_NeverOverflows()
    {
        ILoginLockout lockout = Lockout(new LoginLockoutOptions
        {
            Threshold = 1,
            InitialLockDuration = TimeSpan.FromTicks(1),
            MaxLockDuration = TimeSpan.MaxValue,
        });

        lockout.LockFor(1).Should().Be(TimeSpan.FromTicks(1));
        lockout.LockFor(63).Should().Be(TimeSpan.FromTicks(1L << 62));
        lockout.LockFor(64).Should().Be(TimeSpan.MaxValue);
        lockout.LockFor(int.MaxValue).Should().Be(TimeSpan.MaxValue);
    }

    [TestMethod]
    public void LockedUntilAfter_BelowThreshold_IsNull()
        => Lockout(clock: Clock(Now)).LockedUntilAfter(4).Should().BeNull();

    [TestMethod]
    [DataRow(5, 1)]
    [DataRow(7, 4)]
    [DataRow(100, 15)]
    public void LockedUntilAfter_FromThreshold_IsNowPlusTheLock(int failures, int minutes)
        => Lockout(clock: Clock(Now)).LockedUntilAfter(failures).Should().Be(Now + TimeSpan.FromMinutes(minutes));

    [TestMethod]
    public void LockedUntilAfter_ReadsTheClockOnEachCall()
    {
        IClock clock = Clock(Now);
        ILoginLockout lockout = Lockout(clock: clock);
        lockout.LockedUntilAfter(5).Should().Be(Now.AddMinutes(1));

        clock.UtcNow.Returns(Now.AddHours(1));

        lockout.LockedUntilAfter(5).Should().Be(Now.AddHours(1).AddMinutes(1));
    }

    [TestMethod]
    public void LockedUntilAfter_LockEndingAfterTheLastInstant_EndsAtTheLastInstant()
        => Lockout(clock: Clock(DateTimeOffset.MaxValue.AddSeconds(-30))).LockedUntilAfter(5)
            .Should().Be(DateTimeOffset.MaxValue);

    [TestMethod]
    public void LockedUntilAfter_LockEndingExactlyAtTheLastInstant_EndsThere()
        => Lockout(clock: Clock(DateTimeOffset.MaxValue.AddMinutes(-1))).LockedUntilAfter(5)
            .Should().Be(DateTimeOffset.MaxValue);

    [TestMethod]
    public void LockedUntilAfter_ClockNearTheEndWithAPositiveOffset_AddsInUtc()
    {
        var lastMinuteAtPlusOne = new DateTimeOffset(9999, 12, 31, 23, 59, 30, TimeSpan.FromHours(1));

        DateTimeOffset? until = Lockout(clock: Clock(lastMinuteAtPlusOne)).LockedUntilAfter(5);

        until.Should().Be(new DateTimeOffset(9999, 12, 31, 23, 0, 30, TimeSpan.Zero));
        until?.Offset.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void LockedUntilAfter_NegativeCount_Throws()
    {
        ILoginLockout lockout = Lockout(clock: Clock(Now));

        Action act = () => lockout.LockedUntilAfter(-1);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("consecutiveFailures");
    }

    [TestMethod]
    public void IsLocked_NoLock_IsFalse()
        => Lockout(clock: Clock(Now)).IsLocked(null).Should().BeFalse();

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(0, false)]
    [DataRow(-1, false)]
    public void IsLocked_LockEnd_RunsOnlyWhileItLiesAhead(int offsetTicks, bool expected)
        => Lockout(clock: Clock(Now)).IsLocked(Now.AddTicks(offsetTicks)).Should().Be(expected);

    [TestMethod]
    public void IsLocked_LockEndInAnotherOffset_ComparesTheInstant()
    {
        var sameInstantElsewhere = new DateTimeOffset(2026, 3, 1, 14, 0, 0, TimeSpan.FromHours(2));

        Lockout(clock: Clock(Now)).IsLocked(sameInstantElsewhere).Should().BeFalse();
        Lockout(clock: Clock(Now)).IsLocked(sameInstantElsewhere.AddSeconds(1)).Should().BeTrue();
    }

    [TestMethod]
    public void IsLocked_AfterLockedUntilAfter_RunsUntilTheEnd()
    {
        IClock clock = Clock(Now);
        ILoginLockout lockout = Lockout(clock: clock);
        DateTimeOffset? until = lockout.LockedUntilAfter(6);

        lockout.IsLocked(until).Should().BeTrue();
        clock.UtcNow.Returns(Now.AddMinutes(2).AddTicks(-1));
        lockout.IsLocked(until).Should().BeTrue();
        clock.UtcNow.Returns(Now.AddMinutes(2));
        lockout.IsLocked(until).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0, 60, 900, nameof(LoginLockoutOptions.Threshold))]
    [DataRow(-3, 60, 900, nameof(LoginLockoutOptions.Threshold))]
    [DataRow(5, 0, 900, nameof(LoginLockoutOptions.InitialLockDuration))]
    [DataRow(5, -1, 900, nameof(LoginLockoutOptions.InitialLockDuration))]
    [DataRow(5, 60, 59, nameof(LoginLockoutOptions.MaxLockDuration))]
    public void Resolve_InvalidOptions_FailsWithValidation(int threshold, int initialSeconds, int maxSeconds, string member)
    {
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(new LoginLockoutOptions
        {
            Threshold = threshold,
            InitialLockDuration = TimeSpan.FromSeconds(initialSeconds),
            MaxLockDuration = TimeSpan.FromSeconds(maxSeconds),
        }));

        Action act = () => services.GetRequiredService<ILoginLockout>();

        CauseOf(act.Should().Throw<InvalidOperationException>().Which)
            .Should().BeOfType<ValidationException>().Which.ValidationResult.MemberNames.Should().Equal(member);
    }

    [TestMethod]
    public void Options_Defaults_AreFiveFailuresOneMinuteFifteenMinutes()
    {
        var options = new LoginLockoutOptions();

        options.Threshold.Should().Be(5);
        options.InitialLockDuration.Should().Be(TimeSpan.FromMinutes(1));
        options.MaxLockDuration.Should().Be(TimeSpan.FromMinutes(15));
        options.Validate(new ValidationContext(options)).Should().AllSatisfy(result => result.Should().BeNull());
    }

    [TestMethod]
    public void Container_ResolvesOneInstanceWithTheDefaultOptions()
    {
        IServiceProvider services = GetServices();

        services.GetRequiredService<ILoginLockout>().Should().BeSameAs(services.GetRequiredService<ILoginLockout>());
        services.GetRequiredService<LoginLockoutOptions>().Should().Be(new LoginLockoutOptions());
    }

    [TestMethod]
    public void Container_OptionsRegisteredBeforeTheModule_AreKept()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new LoginLockoutOptions { Threshold = 2 });
        builder.RegisterModule<Common.Module>();
        using IContainer container = builder.Build();

        container.Resolve<ILoginLockout>().LockFor(2).Should().Be(TimeSpan.FromMinutes(1));
        container.Resolve<IEnumerable<LoginLockoutOptions>>().Should().ContainSingle();
    }

    private ILoginLockout Lockout(LoginLockoutOptions? options = null, IClock? clock = null)
        => GetServices(builder =>
            {
                if (options is not null)
                {
                    builder.RegisterInstance(options);
                }

                if (clock is not null)
                {
                    builder.RegisterInstance(clock).As<IClock>();
                }
            })
            .GetRequiredService<ILoginLockout>();

    private static IClock Clock(DateTimeOffset now)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    /// <summary>
    /// The cause of a failed <c>GetRequiredService</c>: Microsoft's service-provider contract wraps Autofac's
    /// resolution failure in an <see cref="InvalidOperationException"/>.
    /// </summary>
    private static Exception CauseOf(InvalidOperationException thrown)
        => (thrown.InnerException ?? thrown).UnwrapResolutionFailures();
}
