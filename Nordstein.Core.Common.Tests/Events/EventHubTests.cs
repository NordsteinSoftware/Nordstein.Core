using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Events;
using Nordstein.Core.Common.Tests.Events.Internal;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests.Events;

[TestClass]
public sealed class EventHubTests : BaseTest<Module>
{
    private static DateTimeOffset Start => new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    private static long FirstId => (Start.ToUnixTimeMilliseconds() * 1000) + 1;

    [TestMethod]
    public async Task Publish_ToASubscriber_DeliversInOrderWithRisingIds()
    {
        IEventHub<SampleEvent> hub = Hub();
        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true);

        hub.Publish(new SampleEvent("a", 1));
        hub.Publish(new SampleEvent("a", 2));
        hub.Publish(new SampleEvent("b", 3));

        PublishedEvent<SampleEvent>[] received = await Read(subscription, 3);
        received.Select(e => e.Event.Number).Should().Equal(1, 2, 3);
        received.Select(e => e.Id).Should().Equal(FirstId, FirstId + 1, FirstId + 2);
        subscription.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task Publish_SameEventToTwoSubscribers_SharesOnePublishedEvent()
    {
        IEventHub<SampleEvent> hub = Hub();
        using IEventSubscription<SampleEvent> first = hub.Subscribe(_ => true);
        using IEventSubscription<SampleEvent> second = hub.Subscribe(_ => true);
        var sample = new SampleEvent("a", 1);

        hub.Publish(sample);

        PublishedEvent<SampleEvent> one = (await Read(first, 1))[0];
        PublishedEvent<SampleEvent> two = (await Read(second, 1))[0];
        one.Should().BeSameAs(two);
        one.Event.Should().BeSameAs(sample);
    }

    [TestMethod]
    public void Publish_NoSubscriber_DoesNotThrow()
    {
        IEventHub<SampleEvent> hub = Hub();

        Action act = () => hub.Publish(new SampleEvent("a", 1));

        act.Should().NotThrow();
    }

    [TestMethod]
    public async Task Ids_ASecondHubStartedLater_IssuesGreaterIds()
    {
        DateTimeOffset now = Start;
        IEventHub<SampleEvent> before = Hub(Clock(() => now), new EventHubOptions { SubscriptionCapacity = 1000 });
        using IEventSubscription<SampleEvent> beforeSubscription = before.Subscribe(_ => true);
        for (int i = 0; i < 500; i++)
        {
            before.Publish(new SampleEvent("a", i));
        }

        long lastBefore = (await Read(beforeSubscription, 500))[^1].Id;
        now = Start.AddSeconds(1);
        IEventHub<SampleEvent> after = Hub(Clock(() => now));
        using IEventSubscription<SampleEvent> afterSubscription = after.Subscribe(_ => true);
        after.Publish(new SampleEvent("a", 0));

        (await Read(afterSubscription, 1))[0].Id.Should().BeGreaterThan(lastBefore);
    }

    [TestMethod]
    public async Task Filter_OnlyAcceptedEvents_AreDelivered()
    {
        IEventHub<SampleEvent> hub = Hub();
        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(e => e.Topic == "mine");

        hub.Publish(new SampleEvent("other", 1));
        hub.Publish(new SampleEvent("mine", 2));
        hub.Publish(new SampleEvent("other", 3));
        hub.Publish(new SampleEvent("mine", 4));

        PublishedEvent<SampleEvent>[] received = await Read(subscription, 2);
        received.Select(e => e.Event.Number).Should().Equal(2, 4);
        received.Select(e => e.Id).Should().Equal(FirstId + 1, FirstId + 3);
        subscription.Events.TryRead(out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task ReplaceFilter_AppliesToLaterEventsAndKeepsBufferedOnes()
    {
        IEventHub<SampleEvent> hub = Hub();
        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(e => e.Topic == "a");
        hub.Publish(new SampleEvent("a", 1));

        subscription.ReplaceFilter(e => e.Topic == "b");
        hub.Publish(new SampleEvent("a", 2));
        hub.Publish(new SampleEvent("b", 3));

        (await Read(subscription, 2)).Select(e => e.Event.Number).Should().Equal(1, 3);
    }

    [TestMethod]
    public void ReplaceFilter_Null_ThrowsArgumentNull()
    {
        using IEventSubscription<SampleEvent> subscription = Hub().Subscribe(_ => true);
        Func<SampleEvent, bool> missing = (new Func<SampleEvent, bool>[1])[0];

        Action act = () => subscription.ReplaceFilter(missing);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("filter");
    }

    [TestMethod]
    public void Subscribe_NullFilter_ThrowsArgumentNull()
    {
        IEventHub<SampleEvent> hub = Hub();
        Func<SampleEvent, bool> missing = (new Func<SampleEvent, bool>[1])[0];

        Action act = () => hub.Subscribe(missing);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("filter");
    }

    [TestMethod]
    public void Publish_Null_ThrowsArgumentNull()
    {
        IEventHub<SampleEvent> hub = Hub();
        SampleEvent missing = (new SampleEvent[1])[0];

        Action act = () => hub.Publish(missing);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
    }

    [TestMethod]
    public async Task Subscribe_AfterAnId_ReplaysTheLaterAcceptedEventsThenContinuesLive()
    {
        IEventHub<SampleEvent> hub = Hub();
        for (int i = 1; i <= 5; i++)
        {
            hub.Publish(new SampleEvent(i % 2 == 0 ? "even" : "odd", i));
        }

        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(e => e.Topic == "odd", afterEventId: FirstId);
        hub.Publish(new SampleEvent("odd", 7));

        PublishedEvent<SampleEvent>[] received = await Read(subscription, 3);
        received.Select(e => e.Event.Number).Should().Equal(3, 5, 7);
        received.Select(e => e.Id).Should().Equal(FirstId + 2, FirstId + 4, FirstId + 5);
        subscription.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task Subscribe_WithoutAnId_ReplaysNothing()
    {
        IEventHub<SampleEvent> hub = Hub();
        hub.Publish(new SampleEvent("a", 1));

        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true);
        hub.Publish(new SampleEvent("a", 2));

        (await Read(subscription, 1))[0].Event.Number.Should().Be(2);
        subscription.Events.TryRead(out _).Should().BeFalse();
    }

    [TestMethod]
    public void Subscribe_ReplayLargerThanTheLiveCapacity_ReplaysAllOfIt()
    {
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { SubscriptionCapacity = 2, ReplayCapacity = 100 });
        for (int i = 1; i <= 50; i++)
        {
            hub.Publish(new SampleEvent("a", i));
        }

        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true, afterEventId: FirstId - 1);
        hub.Publish(new SampleEvent("a", 51));
        hub.Publish(new SampleEvent("a", 52));

        Drain(subscription).Select(e => e.Event.Number).Should().Equal(Enumerable.Range(1, 52));
        subscription.Events.Completion.IsCompleted.Should().BeFalse();
    }

    [TestMethod]
    public void Subscribe_AtTheCapacityBoundary_ReplaysWhatIsKeptAndTellsWhetherItIsComplete()
    {
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { ReplayCapacity = 3 });
        for (int i = 1; i <= 5; i++)
        {
            hub.Publish(new SampleEvent("a", i));
        }

        // Kept: 3, 4, 5. Event 2 was the last to leave the history.
        using IEventSubscription<SampleEvent> afterFirst = hub.Subscribe(_ => true, afterEventId: FirstId);
        using IEventSubscription<SampleEvent> afterSecond = hub.Subscribe(_ => true, afterEventId: FirstId + 1);
        using IEventSubscription<SampleEvent> afterLast = hub.Subscribe(_ => true, afterEventId: FirstId + 4);

        Drain(afterFirst).Select(e => e.Event.Number).Should().Equal(3, 4, 5);
        afterFirst.ReplayIncomplete.Should().BeTrue("event 2 came after the cursor and is gone");
        Drain(afterSecond).Select(e => e.Event.Number).Should().Equal(3, 4, 5);
        afterSecond.ReplayIncomplete.Should().BeFalse("nothing after the cursor left the history");
        Drain(afterLast).Should().BeEmpty();
        afterLast.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public void Subscribe_AtTheWindowBoundary_DropsOlderEventsAndTellsWhetherItIsComplete()
    {
        DateTimeOffset now = Start;
        IEventHub<SampleEvent> hub = Hub(Clock(() => now));
        hub.Publish(new SampleEvent("a", 1));
        now = Start.AddMinutes(3);
        hub.Publish(new SampleEvent("a", 2));
        now = Start.AddMinutes(5);

        using IEventSubscription<SampleEvent> exactlyAtTheWindow = hub.Subscribe(_ => true, afterEventId: FirstId - 1);
        now = Start.AddMinutes(5).AddTicks(1);
        using IEventSubscription<SampleEvent> pastTheWindow = hub.Subscribe(_ => true, afterEventId: FirstId - 1);
        using IEventSubscription<SampleEvent> afterThePruned = hub.Subscribe(_ => true, afterEventId: FirstId);

        Drain(exactlyAtTheWindow).Select(e => e.Event.Number).Should().Equal(1, 2);
        exactlyAtTheWindow.ReplayIncomplete.Should().BeFalse();
        Drain(pastTheWindow).Select(e => e.Event.Number).Should().Equal(2);
        pastTheWindow.ReplayIncomplete.Should().BeTrue();
        Drain(afterThePruned).Select(e => e.Event.Number).Should().Equal(2);
        afterThePruned.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public void Subscribe_IdFromBeforeTheHubStarted_ReplaysEverythingKeptAndIsIncomplete()
    {
        IEventHub<SampleEvent> hub = Hub();
        hub.Publish(new SampleEvent("a", 1));

        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true, afterEventId: FirstId - 1000);

        Drain(subscription).Select(e => e.Event.Number).Should().Equal(1);
        subscription.ReplayIncomplete.Should().BeTrue("a previous process may have published after that id");
    }

    [TestMethod]
    public void Subscribe_IdThisHubNeverIssued_ReplaysNothingAndIsIncomplete()
    {
        IEventHub<SampleEvent> hub = Hub();
        hub.Publish(new SampleEvent("a", 1));

        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true, afterEventId: FirstId + 1);

        Drain(subscription).Should().BeEmpty();
        subscription.ReplayIncomplete.Should().BeTrue();
    }

    [TestMethod]
    public void Subscribe_ImmediatelyBeforeTheFirstId_IsComplete()
    {
        IEventHub<SampleEvent> hub = Hub();

        using IEventSubscription<SampleEvent> beforeAnything = hub.Subscribe(_ => true, afterEventId: FirstId - 1);

        beforeAnything.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public void Subscribe_ThrowingFilterDuringTheReplay_PropagatesAndOpensNothing()
    {
        IEventHub<SampleEvent> hub = Hub();
        hub.Publish(new SampleEvent("a", 1));

        Action act = () => hub.Subscribe(_ => throw new InvalidDataException("filter failed"), afterEventId: FirstId - 1);

        act.Should().Throw<InvalidDataException>();
        Action publish = () => hub.Publish(new SampleEvent("a", 2));
        publish.Should().NotThrow();
    }

    [TestMethod]
    public async Task Publish_SlowSubscriber_EndsOnlyThatSubscriptionAfterItsBufferedEvents()
    {
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { SubscriptionCapacity = 2 });
        using IEventSubscription<SampleEvent> slow = hub.Subscribe(_ => true);
        using IEventSubscription<SampleEvent> fast = hub.Subscribe(_ => true);

        hub.Publish(new SampleEvent("a", 1));
        hub.Publish(new SampleEvent("a", 2));
        (await Read(fast, 2)).Should().HaveCount(2);
        Action third = () => hub.Publish(new SampleEvent("a", 3));
        third.Should().NotThrow();
        hub.Publish(new SampleEvent("a", 4));

        List<int> delivered = [];
        Func<Task> readAll = async () =>
        {
            await foreach (PublishedEvent<SampleEvent> published in slow.Events.ReadAllAsync(CancellationToken))
            {
                delivered.Add(published.Event.Number);
            }
        };
        EventSubscriptionOverflowException overflow = (await readAll.Should().ThrowAsync<EventSubscriptionOverflowException>()).Which;
        overflow.Capacity.Should().Be(2);
        delivered.Should().Equal(1, 2);
        (await Read(fast, 2)).Select(e => e.Event.Number).Should().Equal(3, 4);
    }

    [TestMethod]
    public async Task Publish_SlowSubscriber_ReadAsyncReportsTheOverflowAsTheInnerException()
    {
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { SubscriptionCapacity = 1 });
        using IEventSubscription<SampleEvent> slow = hub.Subscribe(_ => true);
        hub.Publish(new SampleEvent("a", 1));
        hub.Publish(new SampleEvent("a", 2));

        slow.Events.TryRead(out PublishedEvent<SampleEvent>? first).Should().BeTrue();
        first?.Event.Number.Should().Be(1);
        Func<Task> read = async () => await slow.Events.ReadAsync(CancellationToken);

        (await read.Should().ThrowAsync<ChannelClosedException>()).WithInnerException<EventSubscriptionOverflowException>();
    }

    [TestMethod]
    public void Subscribe_AgainAfterAnOverflow_ReplaysWhatTheSubscriberMissed()
    {
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { SubscriptionCapacity = 2 });
        using IEventSubscription<SampleEvent> slow = hub.Subscribe(_ => true);
        for (int i = 1; i <= 5; i++)
        {
            hub.Publish(new SampleEvent("a", i));
        }

        long lastProcessed = Drain(slow)[^1].Id;
        using IEventSubscription<SampleEvent> resumed = hub.Subscribe(_ => true, afterEventId: lastProcessed);

        Drain(resumed).Select(e => e.Event.Number).Should().Equal(3, 4, 5);
        resumed.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task Publish_ThrowingFilter_EndsOnlyThatSubscriptionWithTheFiltersException()
    {
        IEventHub<SampleEvent> hub = Hub();
        var failure = new InvalidDataException("filter failed");
        using IEventSubscription<SampleEvent> failing = hub.Subscribe(e => e.Number == 2 ? throw failure : true);
        using IEventSubscription<SampleEvent> healthy = hub.Subscribe(_ => true);

        hub.Publish(new SampleEvent("a", 1));
        Action second = () => hub.Publish(new SampleEvent("a", 2));
        second.Should().NotThrow();
        hub.Publish(new SampleEvent("a", 3));

        Drain(failing).Select(e => e.Event.Number).Should().Equal(1);
        (await FailureOf(failing.Events.Completion)).Should().BeSameAs(failure);
        (await Read(healthy, 3)).Select(e => e.Event.Number).Should().Equal(1, 2, 3);
    }

    [TestMethod]
    public async Task Publish_FilterThatCallsTheHub_FailsThatSubscriptionInsteadOfDeadlocking()
    {
        IEventHub<SampleEvent> hub = Hub();
        using IEventSubscription<SampleEvent> reentrant = hub.Subscribe(e =>
        {
            hub.Publish(new SampleEvent("nested", e.Number));
            return true;
        });

        Task publish = Task.Run(() => hub.Publish(new SampleEvent("a", 1)), CancellationToken);

        (await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken))).Should().BeSameAs(publish, "a reentrant call must not deadlock");
        await publish;
        (await FailureOf(reentrant.Events.Completion))
            .Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Publish_FilterThatDisposesItsSubscription_FailsThatSubscriptionInsteadOfDeadlocking()
    {
        IEventHub<SampleEvent> hub = Hub();
        IEventSubscription<SampleEvent>? self = null;
        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ =>
        {
            self?.Dispose();
            return true;
        });
        self = subscription;

        Task publish = Task.Run(() => hub.Publish(new SampleEvent("a", 1)), CancellationToken);

        (await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken))).Should().BeSameAs(publish);
        await publish;
        (await FailureOf(subscription.Events.Completion))
            .Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Dispose_Subscription_CompletesItsEventsAndStopsDelivery()
    {
        IEventHub<SampleEvent> hub = Hub();
        IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true);
        hub.Publish(new SampleEvent("a", 1));

        subscription.Dispose();
        subscription.Dispose();
        hub.Publish(new SampleEvent("a", 2));

        List<int> delivered = [];
        await foreach (PublishedEvent<SampleEvent> published in subscription.Events.ReadAllAsync(CancellationToken))
        {
            delivered.Add(published.Event.Number);
        }

        delivered.Should().Equal(1);
    }

    [TestMethod]
    public async Task ReadAsync_Cancelled_ThrowsAndLeavesTheSubscriptionIntact()
    {
        IEventHub<SampleEvent> hub = Hub();
        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true);
        using var cancellation = new CancellationTokenSource();
        ValueTask<PublishedEvent<SampleEvent>> waiting = subscription.Events.ReadAsync(cancellation.Token);
        waiting.IsCompleted.Should().BeFalse();

        await cancellation.CancelAsync();
        Func<Task> act = async () => await waiting;

        await act.Should().ThrowAsync<OperationCanceledException>();
        hub.Publish(new SampleEvent("a", 1));
        (await Read(subscription, 1))[0].Event.Number.Should().Be(1);
    }

    [TestMethod]
    public async Task Dispose_Hub_CompletesEverySubscriptionAndIgnoresLaterPublishes()
    {
        IContainer container = Container();
        var hub = container.Resolve<IEventHub<SampleEvent>>();
        IEventSubscription<SampleEvent> first = hub.Subscribe(_ => true);
        IEventSubscription<SampleEvent> second = hub.Subscribe(_ => true);
        hub.Publish(new SampleEvent("a", 1));

        await container.DisposeAsync();
        Action publish = () => hub.Publish(new SampleEvent("a", 2));
        Action subscribe = () => hub.Subscribe(_ => true);

        publish.Should().NotThrow();
        subscribe.Should().Throw<ObjectDisposedException>();

        // Completion finishes once the channel is completed and drained.
        Drain(first).Select(e => e.Event.Number).Should().Equal(1);
        Drain(second).Select(e => e.Event.Number).Should().Equal(1);
        await Bounded(first.Events.Completion);
        await Bounded(second.Events.Completion);
        first.Dispose();
        ((IDisposable)hub).Dispose();
    }

    [TestMethod]
    public async Task Publish_ConcurrentPublishers_EveryEventOnceWithContiguousIdsAndPerPublisherOrder()
    {
        const int Publishers = 8;
        const int PerPublisher = 500;
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { SubscriptionCapacity = Publishers * PerPublisher });
        using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true);
        using var start = new ManualResetEventSlim();

        Task[] publishers = [.. Enumerable.Range(0, Publishers).Select(p => Task.Run(() =>
        {
            start.Wait(CancellationToken);
            for (int i = 0; i < PerPublisher; i++)
            {
                hub.Publish(new SampleEvent($"p{p}", i));
            }
        }, CancellationToken))];
        start.Set();
        await Bounded(Task.WhenAll(publishers));

        PublishedEvent<SampleEvent>[] received = Drain(subscription);
        received.Should().HaveCount(Publishers * PerPublisher);
        received.Select(e => e.Id).Should().Equal(Enumerable.Range(0, Publishers * PerPublisher).Select(i => FirstId + i));
        foreach (IGrouping<string, PublishedEvent<SampleEvent>> publisher in received.GroupBy(e => e.Event.Topic))
        {
            publisher.Select(e => e.Event.Number).Should().Equal(Enumerable.Range(0, PerPublisher));
        }
    }

    [TestMethod]
    public async Task Subscribe_WhilePublishing_ReplayAndLiveJoinWithoutDuplicateReorderOrGap()
    {
        const int Total = 3000;
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { ReplayCapacity = Total, SubscriptionCapacity = Total });
        using IEventSubscription<SampleEvent> observer = hub.Subscribe(_ => true);
        Task publisher = Task.Run(() =>
        {
            for (int i = 0; i < Total; i++)
            {
                hub.Publish(new SampleEvent("a", i));
            }
        }, CancellationToken);
        PublishedEvent<SampleEvent> seen = (await Read(observer, 1))[0];

        using IEventSubscription<SampleEvent> joined = hub.Subscribe(_ => true, afterEventId: seen.Id);
        await Bounded(publisher);

        long[] ids = [.. Drain(joined).Select(e => e.Id)];
        ids.Should().Equal(Enumerable.Range(0, (int)(FirstId + Total - 1 - seen.Id)).Select(i => seen.Id + 1 + i));
        joined.ReplayIncomplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task Subscriptions_OpenedAndDisposedWhilePublishing_NeverFailAPublisher()
    {
        IEventHub<SampleEvent> hub = Hub(options: new EventHubOptions { ReplayCapacity = 100, SubscriptionCapacity = 16 });
        using var stop = new CancellationTokenSource();
        Task publisher = Task.Run(() =>
        {
            int i = 0;
            while (!stop.IsCancellationRequested)
            {
                hub.Publish(new SampleEvent("a", i++));
            }
        }, CancellationToken);
        Task[] churners = [.. Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                using IEventSubscription<SampleEvent> subscription = hub.Subscribe(_ => true, afterEventId: FirstId);
                subscription.Events.TryRead(out PublishedEvent<SampleEvent>? _);
            }
        }, CancellationToken))];

        await Bounded(Task.WhenAll(churners));
        await stop.CancelAsync();
        await Bounded(publisher);

        using IEventSubscription<SampleEvent> last = hub.Subscribe(_ => true);
        hub.Publish(new SampleEvent("a", -1));
        (await Read(last, 1))[0].Event.Number.Should().Be(-1);
    }

    [TestMethod]
    public async Task Dispose_HubWhilePublishing_CompletesReadersAndNeverFailsAPublisher()
    {
        IContainer container = Container(new EventHubOptions { SubscriptionCapacity = 10_000 });
        var hub = container.Resolve<IEventHub<SampleEvent>>();
        IEventSubscription<SampleEvent>[] subscriptions = [.. Enumerable.Range(0, 4).Select(_ => hub.Subscribe(_ => true))];
        using var start = new ManualResetEventSlim();
        Task[] publishers = [.. Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait(CancellationToken);
            for (int i = 0; i < 2000; i++)
            {
                hub.Publish(new SampleEvent("a", i));
            }
        }, CancellationToken))];

        start.Set();
        await container.DisposeAsync();
        await Bounded(Task.WhenAll(publishers));

        foreach (IEventSubscription<SampleEvent> subscription in subscriptions)
        {
            long[] ids = [.. Drain(subscription).Select(e => e.Id)];
            ids.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            Func<Task> completion = () => Bounded(subscription.Events.Completion);
            await completion.Should().NotThrowAsync("disposing the hub completes a subscription normally");
        }
    }

    [TestMethod]
    public void Hubs_OfTwoEventTypes_AreSeparateWithTheirOwnIds()
    {
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(Clock(() => Start)).As<IClock>());
        var samples = services.GetRequiredService<IEventHub<SampleEvent>>();
        var others = services.GetRequiredService<IEventHub<OtherEvent>>();
        using IEventSubscription<SampleEvent> sampleSubscription = samples.Subscribe(_ => true);
        using IEventSubscription<OtherEvent> otherSubscription = others.Subscribe(_ => true);

        samples.Publish(new SampleEvent("a", 1));
        others.Publish(new OtherEvent(1));

        Drain(sampleSubscription).Single().Id.Should().Be(FirstId);
        Drain(otherSubscription).Single().Id.Should().Be(FirstId);
        services.GetRequiredService<IEventHub<SampleEvent>>().Should().BeSameAs(samples);
    }

    [TestMethod]
    [DataRow(0, 10, 10, nameof(EventHubOptions.ReplayWindow))]
    [DataRow(-1, 10, 10, nameof(EventHubOptions.ReplayWindow))]
    [DataRow(60, 0, 10, nameof(EventHubOptions.ReplayCapacity))]
    [DataRow(60, 10, 0, nameof(EventHubOptions.SubscriptionCapacity))]
    [DataRow(60, 10, -5, nameof(EventHubOptions.SubscriptionCapacity))]
    public void Resolve_InvalidOptions_FailsWithValidation(int windowSeconds, int replayCapacity, int subscriptionCapacity, string member)
    {
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(new EventHubOptions
        {
            ReplayWindow = TimeSpan.FromSeconds(windowSeconds),
            ReplayCapacity = replayCapacity,
            SubscriptionCapacity = subscriptionCapacity,
        }));

        Action act = () => services.GetRequiredService<IEventHub<SampleEvent>>();

        Exception cause = (act.Should().Throw<InvalidOperationException>().Which.InnerException
            ?? throw new InvalidOperationException("no cause")).UnwrapResolutionFailures();
        cause.Should().BeOfType<ValidationException>().Which.ValidationResult.MemberNames.Should().Equal(member);
    }

    [TestMethod]
    public void Options_Defaults_AreFiveMinutesTenThousandAnd256()
    {
        var options = new EventHubOptions();

        options.ReplayWindow.Should().Be(TimeSpan.FromMinutes(5));
        options.ReplayCapacity.Should().Be(10_000);
        options.SubscriptionCapacity.Should().Be(256);
    }

    private IEventHub<SampleEvent> Hub(IClock? clock = null, EventHubOptions? options = null)
        => GetServices(builder =>
            {
                builder.RegisterInstance(clock ?? Clock(() => Start)).As<IClock>();
                if (options is not null)
                {
                    builder.RegisterInstance(options);
                }
            })
            .GetRequiredService<IEventHub<SampleEvent>>();

    /// <summary>
    /// A container this test disposes itself, to observe the hub's disposal.
    /// </summary>
    private static IContainer Container(EventHubOptions? options = null)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<Common.Module>();
        builder.RegisterInstance(Clock(() => Start)).As<IClock>();
        if (options is not null)
        {
            builder.RegisterInstance(options);
        }

        return builder.Build();
    }

    private static IClock Clock(Func<DateTimeOffset> now)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now());
        return clock;
    }

    private async Task<PublishedEvent<SampleEvent>[]> Read(IEventSubscription<SampleEvent> subscription, int count)
    {
        // Bounded, so a regression fails the test instead of hanging the run.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(10));
        var received = new PublishedEvent<SampleEvent>[count];
        for (int i = 0; i < count; i++)
        {
            received[i] = await subscription.Events.ReadAsync(bounded.Token);
        }

        return received;
    }

    /// <summary>
    /// <paramref name="task"/>, failing after ten seconds instead of hanging the run.
    /// </summary>
    private Task Bounded(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

    /// <summary>
    /// The exception a channel's <paramref name="completion"/> ended with, or <see langword="null"/> for a normal end.
    /// </summary>
    private async Task<Exception?> FailureOf(Task completion)
    {
        try
        {
            await Bounded(completion);
            return null;
        }
        catch (Exception exception) when (exception is not TimeoutException)
        {
            return exception;
        }
    }

    private static PublishedEvent<TEvent>[] Drain<TEvent>(IEventSubscription<TEvent> subscription)
        where TEvent : notnull
    {
        List<PublishedEvent<TEvent>> drained = [];
        while (subscription.Events.TryRead(out PublishedEvent<TEvent>? published))
        {
            drained.Add(published);
        }

        return [.. drained];
    }
}
