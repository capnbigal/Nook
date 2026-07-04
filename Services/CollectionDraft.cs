namespace Nook.Services;

using Nook.Models;

/// <summary>
/// A not-yet-persisted collection choice captured in the UI (used by the create
/// dialog and by draft-node collection assignment before the node is saved).
/// </summary>
public sealed record CollectionDraft(string Title, CollectionKind Kind, string? Body);
