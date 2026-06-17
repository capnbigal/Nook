using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Nook.Models;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/Account");

        group.MapPost("/Logout", async (
            SignInManager<ApplicationUser> signInManager,
            [Microsoft.AspNetCore.Mvc.FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        return group;
    }
}
