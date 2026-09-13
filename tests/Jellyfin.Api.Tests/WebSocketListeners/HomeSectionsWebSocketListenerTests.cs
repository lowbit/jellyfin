using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.WebSocketListeners;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Net.WebSocketMessages;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.WebSocketListeners;

/// <summary>
/// Covers what <see cref="HomeSectionsWebSocketListener"/> delivers, and to whom. The listener
/// sends on its own queue, so each test waits for the message rather than asserting straight away.
/// </summary>
public sealed class HomeSectionsWebSocketListenerTests : IAsyncDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(5);

    private readonly User _user = new("one", "default", "default");
    private readonly User _otherUser = new("two", "default", "default");
    private readonly Mock<IHomeSectionManager> _manager = new();
    private readonly HomeSectionsWebSocketListener _listener;

    public HomeSectionsWebSocketListenerTests()
    {
        _listener = new HomeSectionsWebSocketListener(NullLogger<HomeSectionsWebSocketListener>.Instance, _manager.Object);
    }

    [Fact]
    public async Task DeliversToTheUserWhoseSectionsChanged()
    {
        var mine = await SubscribeAsync(_user);
        var theirs = await SubscribeAsync(_otherUser);

        Invalidate(_user, false, "resume");

        var message = await mine.NextAsync();
        Assert.False(message.AllStale);
        Assert.Equal(["resume"], message.StaleSectionKeys);
        Assert.Empty(theirs.Messages);
    }

    [Fact]
    public async Task DeliversToEveryConnectionOfThatUser()
    {
        // A user with the home screen open on two devices must see the change on both.
        var first = await SubscribeAsync(_user);
        var second = await SubscribeAsync(_user);

        Invalidate(_user, true);

        Assert.True((await first.NextAsync()).AllStale);
        Assert.True((await second.NextAsync()).AllStale);
    }

    [Fact]
    public async Task MergesChangesThatArriveInsideTheInterval()
    {
        // The first goes out at once; the next two land inside the interval and must not be lost,
        // so they follow as one message once the interval has passed.
        var connection = await SubscribeAsync(_user, intervalMs: 300);

        Invalidate(_user, false, "resume");
        Assert.Equal(["resume"], (await connection.NextAsync()).StaleSectionKeys);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Invalidate(_user, false, "nextup");
        Invalidate(_user, false, "genre");

        var merged = await connection.NextAsync();
        Assert.Equal(["genre", "nextup"], merged.StaleSectionKeys.Order(StringComparer.Ordinal));
        Assert.Equal(2, connection.Messages.Count);
    }

    [Fact]
    public async Task EverythingStaleSwallowsNarrowerChanges()
    {
        var connection = await SubscribeAsync(_user, intervalMs: 300);

        Invalidate(_user, false, "resume");
        await connection.NextAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Invalidate(_user, false, "nextup");
        Invalidate(_user, true);

        var merged = await connection.NextAsync();
        Assert.True(merged.AllStale);
        Assert.Empty(merged.StaleSectionKeys);
    }

    [Fact]
    public async Task StopsDeliveringAfterStop()
    {
        var connection = await SubscribeAsync(_user);
        await _listener.ProcessMessageAsync(new WebSocketMessageInfo
        {
            MessageType = SessionMessageType.HomeSectionsStop,
            Connection = connection.Mock.Object
        });

        Invalidate(_user, true);

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Empty(connection.Messages);
    }

    public async ValueTask DisposeAsync()
    {
        await _listener.DisposeAsync();
    }

    private async Task<Subscriber> SubscribeAsync(User user, long intervalMs = 1000)
    {
        var subscriber = new Subscriber(user);
        await _listener.ProcessMessageAsync(new WebSocketMessageInfo
        {
            MessageType = SessionMessageType.HomeSectionsStart,
            Data = $"0,{intervalMs}",
            Connection = subscriber.Mock.Object
        });

        return subscriber;
    }

    private void Invalidate(User user, bool allStale, params string[] keys)
        => _manager.Raise(x => x.Invalidated += null, this, new HomeSectionInvalidatedEventArgs
        {
            UserId = user.Id,
            AllStale = allStale,
            StaleSectionKeys = keys
        });

    private sealed class Subscriber : IDisposable
    {
        private readonly BlockingCollection<HomeSectionsChangedInfo> _received = new();

        public Subscriber(User user)
        {
            Mock = new Mock<IWebSocketConnection>();
            Mock.SetupGet(x => x.State).Returns(WebSocketState.Open);
            Mock.SetupGet(x => x.AuthorizationInfo).Returns(new AuthorizationInfo { User = user });
            Mock.Setup(x => x.SendAsync(It.IsAny<OutboundWebSocketMessage<HomeSectionsChangedInfo>>(), It.IsAny<CancellationToken>()))
                .Callback<OutboundWebSocketMessage<HomeSectionsChangedInfo>, CancellationToken>((message, _) =>
                {
                    Messages.Add(message.Data!);
                    _received.Add(message.Data!, CancellationToken.None);
                })
                .Returns(Task.CompletedTask);
        }

        public Mock<IWebSocketConnection> Mock { get; }

        public List<HomeSectionsChangedInfo> Messages { get; } = new();

        public void Dispose() => _received.Dispose();

        public Task<HomeSectionsChangedInfo> NextAsync()
            => Task.Run(() =>
            {
                Assert.True(_received.TryTake(out var message, _wait), "No message arrived in time.");
                return message!;
            });
    }
}
