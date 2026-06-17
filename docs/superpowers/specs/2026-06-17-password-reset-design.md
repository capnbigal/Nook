# Nook — Password Reset Flow (on-screen link)

**Date:** 2026-06-17
**Status:** Approved design, ready for implementation planning
**Builds on:** `2026-06-16-nook-users-and-insights.md` (multi-user auth)

## Summary

Add a self-service password reset to Nook's existing ASP.NET Core Identity auth. Because the
app has **no email infrastructure**, the reset link is **displayed on-screen** after the user
enters their email (the same behavior the official Blazor Identity template falls back to when
no `IEmailSender` is registered). Two new static-SSR pages plus a "Forgot password?" link on the
login page. No new services, no database or model changes, no migration.

## Goals

- A signed-out user who forgot their password can set a new one without admin involvement.
- Reuses the existing Identity token machinery (`AddDefaultTokenProviders()` is already registered).
- Matches the existing auth-page pattern (static SSR, MudBlazor chrome, `EditForm` + `InputText`).

## Non-Goals

- Real email delivery (SMTP/SendGrid). Explicitly deferred; see "Future work."
- Email-enumeration protection. Since the reset link is shown on-screen, hiding whether an email
  is registered is pointless; the page states plainly whether an account was found.
- Rate limiting / CAPTCHA / lockout changes.
- Changing password complexity rules (already configured in `Program.cs`).

## Security Note (explicit)

On-screen reset is **not production-grade**: anyone who can reach `/forgot-password` can generate a
reset link for any account and reset its password. This is acceptable for a local/single-machine
app and is documented here so it is a conscious choice, not an oversight. Moving to real email
(see "Future work") closes this gap without changing the page flow.

## Architecture

All work lives under `Components/Account/Pages/`. The pages follow the exact pattern already used
by `Login.razor` / `Register.razor`:

- `@page` route, `@layout Nook.Components.Layout.AuthLayout`
- `@attribute [AllowAnonymous]` and `@attribute [ExcludeFromInteractiveRouting]` (static SSR so the
  form POST writes/reads correctly)
- `<EditForm Model="Input" method="post" OnValidSubmit="..." FormName="...">` with
  `[SupplyParameterFromForm]` input models and plain `<InputText>` for fields (NOT `MudTextField`)
- MudBlazor for chrome (`MudPaper`, `MudText`, `MudButton ButtonType="ButtonType.Submit"`,
  `MudAlert`, `MudLink`)
- The `= new()` initializer on the form-model property needs `#pragma warning disable BL0008`
  (same as Login/Register)

### Token handling

- Generate: `var token = await UserManager.GeneratePasswordResetTokenAsync(user);`
- URL-encode for the link:
  `var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));`
  (`using Microsoft.AspNetCore.WebUtilities;` and `using System.Text;`)
- Decode on the reset page:
  `var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));`
- Reset: `var result = await UserManager.ResetPasswordAsync(user, token, Input.Password);`

## Components

### `Components/Account/Pages/ForgotPassword.razor` — `/forgot-password`
- Injects `UserManager<ApplicationUser>`, `NavigationManager`.
- Form: single **Email** field (`autocomplete="username"`).
- On submit:
  - `var user = await UserManager.FindByEmailAsync(Input.Email);`
  - If `user is null` → set a message: "No account found with that email." (no link).
  - Else → generate + Base64Url-encode the token, build the reset URL from an **absolute** base so
    the query-string helper works reliably:
    `var url = NavigationManager.GetUriWithQueryParameters(NavigationManager.ToAbsoluteUri("reset-password").AbsoluteUri, new Dictionary<string, object?> { ["email"] = Input.Email, ["code"] = code });`
    and display it as a clickable `MudLink Href="@url"` with a short explanation ("Email isn't
    configured, so use this link to reset your password.").
- Link back to `/login`.

### `Components/Account/Pages/ResetPassword.razor` — `/reset-password`
- Injects `UserManager<ApplicationUser>`, `IdentityRedirectManager`.
- Reads `[SupplyParameterFromQuery] Email` and `[SupplyParameterFromQuery(Name = "code")] Code`.
- If `Email` or `Code` is missing/blank on a GET → show "Invalid password reset link." and a link
  to `/forgot-password` (don't render the form).
- Form: **New password** + **Confirm password** (`autocomplete="new-password"`), with a
  **Show password** toggle (same inline-handler pattern added to `Register.razor`:
  `data-pw="true"` on both inputs + a checkbox that toggles their `type`).
- Input model validation: `[Required, StringLength(100, MinimumLength = 6)]` on Password,
  `[Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]` on ConfirmPassword.
- On submit:
  - `var user = await UserManager.FindByEmailAsync(Email);`
  - If `user is null` → show a generic failure ("Could not reset the password.").
  - Else decode the token and `ResetPasswordAsync`; on success
    `RedirectManager.RedirectTo("login")`; on failure add `result.Errors` to an inline `MudAlert`.

### `Components/Account/Pages/Login.razor` — modify
- Add a **"Forgot password?"** `MudLink` to `/forgot-password` (near the existing
  "No account? Create one" link).

## Data Flow

1. `/login` → "Forgot password?" → `/forgot-password`.
2. User submits email → app generates + encodes a reset token → shows the reset link on the page.
3. User clicks the link → `/reset-password?email=…&code=…` → submits a new password.
4. `ResetPasswordAsync` succeeds → redirect to `/login` → user signs in with the new password.

## Error Handling

- Missing/blank `email` or `code` on `/reset-password` → "Invalid password reset link." + link to retry.
- Unknown email (either page) → ForgotPassword: "No account found with that email."; ResetPassword:
  generic "Could not reset the password." (don't reveal which step failed beyond Identity's messages).
- Invalid/expired token → `ResetPasswordAsync` returns errors → shown inline; user can request a new link.
- All redirects use `IdentityRedirectManager` (navigate-and-return; no thrown `NavigationException`,
  consistent with the current config).

## Testing

No new unit-testable service logic is introduced (the flow is `UserManager` calls inside Razor
components, mirroring `Login`/`Register`). Verification is by clean `dotnet build` plus a runtime
walkthrough:

1. `/forgot-password` with the seeded demo email (`demo@nook.local`) returns a reset link containing
   `email` and a non-empty `code`.
2. Visiting that link and submitting a new password redirects to `/login`.
3. Logging in with the **new** password succeeds (302 → `/dashboard`); the old password fails.
4. `/reset-password` with no `code` shows the "Invalid password reset link" state.
5. Build is clean (0 warnings); existing 16 tests still pass.

## Build Sequence (for the implementation plan)

1. `ForgotPassword.razor` (generate + display the encoded reset link).
2. `ResetPassword.razor` (decode token, reset, redirect to login; show-password toggle).
3. Add the "Forgot password?" link to `Login.razor`.
4. Runtime walkthrough + build verification.

## Future Work

- Register an `IEmailSender<ApplicationUser>` and email the reset link instead of displaying it
  (e.g. SMTP/SendGrid). The page flow is unchanged — only the delivery of the link differs. Doing so
  also restores the value of email-enumeration protection (show a neutral "check your email" message
  regardless of whether the account exists).
