using Microsoft.AspNetCore.Identity;

namespace Nook.Models;

/// <summary>The application user. Owns Items, Tags and ActivityLog rows.</summary>
public class ApplicationUser : IdentityUser
{
    // IdentityUser already provides Id (string), UserName, Email, PasswordHash, etc.
}
