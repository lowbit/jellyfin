using System;

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// One row of the home screen as configured by the server administrator.
    /// </summary>
    /// <remarks>
    /// Used for the defaults new users inherit. Once a user changes their own layout their
    /// stored sections win, so editing the defaults later does not overwrite anyone.
    /// </remarks>
    public class HomeSectionOptions
    {
        /// <summary>
        /// Gets or sets the key of the provider that builds this section.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the collection or genre this section is bound to.
        /// </summary>
        public Guid? ItemId { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of items, or null for the server default.
        /// </summary>
        public int? MaxItems { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the section is shown.
        /// </summary>
        public bool Active { get; set; } = true;
    }
}
