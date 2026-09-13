using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities
{
    /// <summary>
    /// An entity representing a section on the user's home page.
    /// </summary>
    public class HomeSection
    {
        /// <summary>
        /// Gets the id.
        /// </summary>
        /// <remarks>
        /// Identity. Required.
        /// </remarks>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; private set; }

        /// <summary>
        /// Gets or sets the Id of the associated display preferences.
        /// </summary>
        /// <remarks>
        /// Required.
        /// </remarks>
        public int DisplayPreferencesId { get; set; }

        /// <summary>
        /// Gets or sets the order.
        /// </summary>
        /// <remarks>
        /// Required.
        /// </remarks>
        public int Order { get; set; }

        /// <summary>
        /// Gets or sets the key of the provider that builds this section.
        /// </summary>
        /// <remarks>
        /// Required. A string rather than an enum because plugins contribute providers, so the set
        /// of keys is open. The built-in ones are the lower-cased legacy type names.
        /// </remarks>
        [MaxLength(64)]
        [StringLength(64)]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the item this section is bound to.
        /// </summary>
        /// <remarks>
        /// The collection for a pinned collection or the genre for a genre row. Null for providers
        /// that take no parameter.
        /// </remarks>
        public Guid? ItemId { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of items to show, or null for the server default.
        /// </summary>
        public int? MaxItems { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the section is shown.
        /// </summary>
        /// <remarks>
        /// Lets a user hide a section without losing its position and settings.
        /// </remarks>
        public bool Active { get; set; } = true;
    }
}
