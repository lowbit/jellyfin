using System.ComponentModel;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Controller.Net.WebSocketMessages.Inbound;

/// <summary>
/// Unsubscribe from home section staleness notifications.
/// </summary>
public class HomeSectionsStopMessage : InboundWebSocketMessage
{
    /// <inheritdoc />
    [DefaultValue(SessionMessageType.HomeSectionsStop)]
    public override SessionMessageType MessageType => SessionMessageType.HomeSectionsStop;
}
