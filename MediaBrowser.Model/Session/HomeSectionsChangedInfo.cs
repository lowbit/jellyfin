using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Session
{
    /// <summary>
    /// Tells a client which of its home sections are out of date.
    /// </summary>
    /// <remarks>
    /// When the client builds the home screen itself it knows what feeds each row, so it can watch
    /// the WebSocket for library and user data changes and refresh only the affected row. Once the
    /// server builds the sections that knowledge moves server-side, so the server has to say which
    /// rows went stale, otherwise a client can only refresh everything or nothing.
    /// </remarks>
    public class HomeSectionsChangedInfo
    {
        /// <summary>
        /// Gets or sets a value indicating whether every section should be considered stale.
        /// </summary>
        /// <remarks>
        /// Set for library and layout changes, where a single added or removed item can affect the
        /// latest row, any genre row it belongs to, and any collection containing it. Working out
        /// the exact set costs more than refetching.
        /// </remarks>
        public bool AllStale { get; set; }

        /// <summary>
        /// Gets or sets the provider keys whose rows went stale.
        /// </summary>
        /// <remarks>
        /// Ignored when <see cref="AllStale"/> is true. Keys match <c>HomeSectionDto.Key</c>, so a
        /// provider that expands to several rows marks all of them at once. Keys rather than row
        /// ids because the server knows which provider an event touches without loading every
        /// user's layout.
        /// </remarks>
        public IReadOnlyList<string> StaleSectionKeys { get; set; } = Array.Empty<string>();
    }
}
