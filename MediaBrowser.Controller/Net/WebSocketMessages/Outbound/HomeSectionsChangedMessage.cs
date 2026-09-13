using System.ComponentModel;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Sent when a user's home sections have gone stale and should be refetched.
/// </summary>
public class HomeSectionsChangedMessage : OutboundWebSocketMessage<HomeSectionsChangedInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionsChangedMessage"/> class.
    /// </summary>
    /// <param name="data">Which sections went stale.</param>
    public HomeSectionsChangedMessage(HomeSectionsChangedInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.HomeSectionsChanged)]
    public override SessionMessageType MessageType => SessionMessageType.HomeSectionsChanged;
}
