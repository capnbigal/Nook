using Microsoft.AspNetCore.Components;

namespace Nook.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    // With <BlazorDisableThrowNavigationException>true</...>, NavigateTo records
    // the redirect on the response and returns instead of throwing a
    // NavigationException — so these helpers simply navigate and return. Call
    // them last in a handler; nothing runs meaningfully after the redirect.
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }
        navigationManager.NavigateTo(uri);
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message,
            new CookieOptions { MaxAge = TimeSpan.FromSeconds(5), HttpOnly = true, IsEssential = true });
        RedirectTo(uri);
    }

    public void RedirectToCurrentPage() => RedirectTo("/");
}
