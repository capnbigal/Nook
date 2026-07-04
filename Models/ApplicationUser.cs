using Microsoft.AspNetCore.Identity;

namespace Nook.Models;

/// <summary>The application user. Owns Nodes, Tags, Actions, Events and ActivityLog rows.</summary>
public class ApplicationUser : IdentityUser
{
    // IdentityUser already provides Id (string), UserName, Email, PasswordHash, etc.

    /// <summary>
    /// Optional "self" Person node, created lazily the first time an event needs
    /// an actor. Never required to use the app.
    /// </summary>
    public int? SelfNodeId { get; set; }
    public Node? SelfNode { get; set; }
}
