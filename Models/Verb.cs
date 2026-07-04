using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A verb describing an event ("met", "watched", "read"). System verbs (UserId
/// null) ship at launch and are shared; user-owned verbs are reserved for future
/// custom vocabulary.
/// </summary>
public class Verb
{
    public int VerbId { get; set; }

    /// <summary>Null = system-defined (shared). Set = owned by a specific user.</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required]
    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public ICollection<EventDetails> Events { get; set; } = new List<EventDetails>();
}
