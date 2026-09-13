using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.WebSocketListeners;

/// <summary>
/// Tells subscribed clients when a user's home sections have gone stale.
/// </summary>
/// <remarks>
/// <para>
/// A client that builds its own home screen knows what feeds each row, so it can watch library and
/// user data events and refresh just that row. Once the server builds the sections that knowledge
/// is no longer on the client, so without this it could only poll or refresh everything.
/// <see cref="IHomeSectionManager"/> decides what went stale; this only delivers it.
/// </para>
/// <para>
/// Unlike the other periodic listeners this sends changes rather than snapshots, so a message
/// skipped inside a client's interval cannot be made up for by the next one. Changes are therefore
/// held per connection and merged until that connection is due, and a flush is scheduled so the
/// merged result goes out even when no further event arrives to trigger it.
/// </para>
/// </remarks>
public class HomeSectionsWebSocketListener : BasePeriodicWebSocketListener<HomeSectionsChangedInfo, WebSocketListenerState>
{
    private const long DefaultIntervalMs = 1000;

    private readonly IHomeSectionManager _homeSectionManager;

    /// <summary>
    /// The interval each subscribed connection asked for.
    /// </summary>
    private readonly ConcurrentDictionary<IWebSocketConnection, long> _subscriptions = new();

    private readonly ConcurrentDictionary<IWebSocketConnection, PendingInvalidation> _pending = new();

    private int _flushScheduled;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionsWebSocketListener"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{HomeSectionsWebSocketListener}"/> interface.</param>
    /// <param name="homeSectionManager">Instance of the <see cref="IHomeSectionManager"/> interface.</param>
    public HomeSectionsWebSocketListener(ILogger<HomeSectionsWebSocketListener> logger, IHomeSectionManager homeSectionManager)
        : base(logger)
    {
        _homeSectionManager = homeSectionManager;
        _homeSectionManager.Invalidated += OnInvalidated;
    }

    /// <inheritdoc />
    protected override SessionMessageType Type => SessionMessageType.HomeSectionsChanged;

    /// <inheritdoc />
    protected override SessionMessageType StartType => SessionMessageType.HomeSectionsStart;

    /// <inheritdoc />
    protected override SessionMessageType StopType => SessionMessageType.HomeSectionsStop;

    /// <inheritdoc />
    protected override Task<HomeSectionsChangedInfo> GetDataToSend()
        => Task.FromResult<HomeSectionsChangedInfo>(null!);

    /// <inheritdoc />
    protected override void Start(WebSocketMessageInfo message)
    {
        var interval = DefaultIntervalMs;
        var parts = message.Data?.Split(',');
        if (parts?.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            interval = parsed;
        }

        _subscriptions[message.Connection] = interval;
        base.Start(message);
    }

    /// <inheritdoc />
    protected override void Stop(WebSocketMessageInfo message)
    {
        _subscriptions.TryRemove(message.Connection, out _);
        _pending.TryRemove(message.Connection, out _);
        base.Stop(message);
    }

    /// <summary>
    /// Gets the pending changes for this connection, if any.
    /// </summary>
    /// <param name="connection">The connection being sent to.</param>
    /// <returns>What went stale for its user, or null to send nothing.</returns>
    protected override Task<HomeSectionsChangedInfo> GetDataToSendForConnection(IWebSocketConnection connection)
    {
        if (!_pending.TryRemove(connection, out var pending))
        {
            // Nothing stale for this connection. The base listener skips the send when this is
            // null, so an unrelated user's playback does not wake every other client.
            return Task.FromResult<HomeSectionsChangedInfo>(null!);
        }

        return Task.FromResult(new HomeSectionsChangedInfo
        {
            AllStale = pending.AllStale,
            StaleSectionKeys = pending.AllStale
                ? Array.Empty<string>()
                : pending.Keys.ToArray()
        });
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (!_disposed)
        {
            _homeSectionManager.Invalidated -= OnInvalidated;
            _disposed = true;
        }

        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private void OnInvalidated(object? sender, HomeSectionInvalidatedEventArgs e)
    {
        var anyPending = false;

        foreach (var (connection, _) in _subscriptions)
        {
            if (connection.State != WebSocketState.Open)
            {
                // A connection that dropped without saying Stop.
                _subscriptions.TryRemove(connection, out _);
                _pending.TryRemove(connection, out _);
                continue;
            }

            var userId = connection.AuthorizationInfo.User?.Id;
            if (!userId.HasValue || !userId.Value.Equals(e.UserId))
            {
                continue;
            }

            // Changes pile up between sends, so a burst during a library scan collapses into one
            // message rather than one per item.
            _pending.AddOrUpdate(
                connection,
                _ => new PendingInvalidation(e),
                (_, existing) =>
                {
                    existing.Merge(e);
                    return existing;
                });
            anyPending = true;
        }

        if (!anyPending)
        {
            return;
        }

        // Goes out now to any connection that is due, and the flush picks up the rest once their
        // interval has passed.
        SendData(false);
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        // One flush at a time. Anything that arrives before it runs is merged into what it sends.
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
        {
            return;
        }

        var interval = _subscriptions.IsEmpty ? DefaultIntervalMs : _subscriptions.Values.Min();
        _ = FlushAfterAsync(interval);
    }

    private async Task FlushAfterAsync(long delayMs)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs)).ConfigureAwait(false);
        Interlocked.Exchange(ref _flushScheduled, 0);

        if (_disposed)
        {
            return;
        }

        SendData(false);

        foreach (var connection in _pending.Keys)
        {
            if (connection.State != WebSocketState.Open)
            {
                _subscriptions.TryRemove(connection, out _);
                _pending.TryRemove(connection, out _);
            }
        }

        // The send runs on the listener's own queue, so what it delivered is not known yet. Keep
        // flushing while anything is held; the next round finds it gone and stops.
        if (!_pending.IsEmpty)
        {
            ScheduleFlush();
        }
    }

    private sealed class PendingInvalidation
    {
        public PendingInvalidation(HomeSectionInvalidatedEventArgs e) => Merge(e);

        public bool AllStale { get; private set; }

        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

        public void Merge(HomeSectionInvalidatedEventArgs e)
        {
            // Once anything has marked everything stale, narrower keys add nothing.
            AllStale |= e.AllStale;

            foreach (var key in e.StaleSectionKeys)
            {
                Keys.Add(key);
            }
        }
    }
}
