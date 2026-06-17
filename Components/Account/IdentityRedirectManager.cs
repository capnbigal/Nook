using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace Nook.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }
        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException(
            $"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    }

    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message,
            new CookieOptions { MaxAge = TimeSpan.FromSeconds(5), HttpOnly = true, IsEssential = true });
        RedirectTo(uri);
    }

    [DoesNotReturn]
    public void RedirectToCurrentPage() => RedirectTo("/");
}
