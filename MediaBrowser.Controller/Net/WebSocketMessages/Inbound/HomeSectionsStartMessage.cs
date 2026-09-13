using System.ComponentModel;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Controller.Net.WebSocketMessages.Inbound;

/// <summary>
/// Subscribe to home section staleness notifications.
/// </summary>
public class HomeSectionsStartMessage : InboundWebSocketMessage<string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionsStartMessage"/> class.
    /// </summary>
    /// <param name="data">Comma separated initial delay and interval in milliseconds.</param>
    public HomeSectionsStartMessage(string data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.HomeSectionsStart)]
    public override SessionMessageType MessageType => SessionMessageType.HomeSectionsStart;
}
