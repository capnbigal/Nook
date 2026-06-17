# Password Reset Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a self-service password reset to Nook that displays the reset link on-screen (no email), via two new static-SSR Identity pages plus a "Forgot password?" link on login.

**Architecture:** Two new Razor components under `Components/Account/Pages/` following the existing Login/Register static-SSR pattern. `/forgot-password` generates a Base64Url-encoded reset token with `UserManager` and shows the reset link. `/reset-password` decodes the token (carried through the POST as hidden fields) and calls `UserManager.ResetPasswordAsync`. No services, no DB/model changes, no migration.

**Tech Stack:** .NET 10 Blazor (static SSR for these pages), ASP.NET Core Identity, MudBlazor 9.5.

## Global Constraints

- Auth pages are **static SSR**: each page has `@page`, `@layout Nook.Components.Layout.AuthLayout`, `@attribute [AllowAnonymous]`, `@attribute [ExcludeFromInteractiveRouting]`.
- Forms use `<EditForm Model="Input" method="post" OnValidSubmit="..." FormName="...">` with `[SupplyParameterFromForm]` input models and plain `<InputText>` (NOT `MudTextField`); MudBlazor only for chrome.
- The `[SupplyParameterFromForm]` model property keeps its `= new()` initializer wrapped in `#pragma warning disable BL0008` / `restore`.
- Token encode: `WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))`; decode: `Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))` (`using Microsoft.AspNetCore.WebUtilities; using System.Text;`).
- Redirects use the existing `IdentityRedirectManager` (navigate-and-return; it does NOT throw — current app config sets `BlazorDisableThrowNavigationException`).
- `email` and `code` MUST survive the reset POST as **hidden form fields** — do NOT rely on the query string being preserved by the form action.
- Build gate: `dotnet build` clean (0 warnings). The existing 16 tests must still pass. No new unit tests (logic is `UserManager` calls in components); verify the flow at runtime.
- Demo account for testing: `demo@nook.local` / `Demo123!`. App URL via `dotnet run`/`dotnet watch`: http://localhost:5176.

---

## Task 1: Forgot-password page

**Files:**
- Create: `Components/Account/Pages/ForgotPassword.razor`

**Interfaces:**
- Produces: anonymous route `/forgot-password`. Generates a reset link to `/reset-password?email=…&code=…`.

- [ ] **Step 1: Create the page**

Create `Components/Account/Pages/ForgotPassword.razor`:

```razor
@page "/forgot-password"
@layout Nook.Components.Layout.AuthLayout
@attribute [AllowAnonymous]
@attribute [ExcludeFromInteractiveRouting]

@using System.ComponentModel.DataAnnotations
@using System.Text
@using Microsoft.AspNetCore.Identity
@using Microsoft.AspNetCore.WebUtilities
@using Nook.Models

@inject UserManager<ApplicationUser> UserManager
@inject NavigationManager Nav

<PageTitle>Forgot password · Nook</PageTitle>

<MudPaper Class="pa-8" Elevation="3">
    <MudText Typo="Typo.h4" GutterBottom="true">Forgot your password?</MudText>
    <MudText Typo="Typo.body2" Class="mb-4 mud-text-secondary">
        Enter your email and we'll generate a reset link.
    </MudText>

    @if (_resetUrl is not null)
    {
        <MudAlert Severity="Severity.Success" Class="mb-4">
            Email isn't configured, so use this link to reset your password:
            <div class="mt-2"><MudLink Href="@_resetUrl">Reset your password</MudLink></div>
        </MudAlert>
    }
    else if (_message is not null)
    {
        <MudAlert Severity="Severity.Info" Class="mb-4">@_message</MudAlert>
    }

    <EditForm Model="Input" method="post" OnValidSubmit="SendResetLink" FormName="forgot-password">
        <DataAnnotationsValidator />
        <div class="mb-3">
            <label class="mud-input-label">Email</label>
            <InputText class="mud-input-root mud-input-root-outlined" @bind-Value="Input.Email"
                       autocomplete="username" style="width:100%;padding:8px;" />
            <ValidationMessage For="() => Input.Email" />
        </div>
        <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary"
                   FullWidth="true" Class="mt-2">Get reset link</MudButton>
    </EditForm>

    <MudText Typo="Typo.body2" Class="mt-4">
        Remembered it? <MudLink Href="/login">Sign in</MudLink>
    </MudText>
</MudPaper>

@code {
#pragma warning disable BL0008
    [SupplyParameterFromForm] private ForgotInput Input { get; set; } = new();
#pragma warning restore BL0008
    private string? _resetUrl;
    private string? _message;

    public async Task SendResetLink()
    {
        _resetUrl = null;
        _message = null;

        var user = await UserManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            _message = "No account found with that email.";
            return;
        }

        var token = await UserManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        _resetUrl = Nav.GetUriWithQueryParameters(
            Nav.ToAbsoluteUri("reset-password").AbsoluteUri,
            new Dictionary<string, object?> { ["email"] = Input.Email, ["code"] = code });
    }

    private sealed class ForgotInput
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeds, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add Components/Account/Pages/ForgotPassword.razor
git commit -m "feat: forgot-password page that shows the reset link on-screen"
```

---

## Task 2: Reset-password page

**Files:**
- Create: `Components/Account/Pages/ResetPassword.razor`

**Interfaces:**
- Consumes: the link produced by Task 1 (`/reset-password?email=…&code=…`).
- Produces: anonymous route `/reset-password`. On success redirects to `/login`.

- [ ] **Step 1: Create the page**

Create `Components/Account/Pages/ResetPassword.razor`. Note: `email`/`code` are seeded from the query on GET and round-trip through the POST as **hidden fields** (`<input type="hidden" name="Input.Email" …>`), so the token is never lost on submit. The `Show password` toggle reuses the inline handler from `Register.razor`.

```razor
@page "/reset-password"
@layout Nook.Components.Layout.AuthLayout
@attribute [AllowAnonymous]
@attribute [ExcludeFromInteractiveRouting]

@using System.ComponentModel.DataAnnotations
@using System.Text
@using Microsoft.AspNetCore.Identity
@using Microsoft.AspNetCore.WebUtilities
@using Nook.Components.Account
@using Nook.Models

@inject UserManager<ApplicationUser> UserManager
@inject IdentityRedirectManager RedirectManager

<PageTitle>Reset password · Nook</PageTitle>

<MudPaper Class="pa-8" Elevation="3">
    <MudText Typo="Typo.h4" GutterBottom="true">Reset password</MudText>

    @if (string.IsNullOrWhiteSpace(Input.Email) || string.IsNullOrWhiteSpace(Input.Code))
    {
        <MudAlert Severity="Severity.Error" Class="mb-4">Invalid password reset link.</MudAlert>
        <MudText Typo="Typo.body2"><MudLink Href="/forgot-password">Request a new link</MudLink></MudText>
    }
    else
    {
        @if (_errors.Count > 0)
        {
            <MudAlert Severity="Severity.Error" Class="mb-4">
                @foreach (var e in _errors) { <div>@e</div> }
            </MudAlert>
        }

        <EditForm Model="Input" method="post" OnValidSubmit="ResetUser" FormName="reset-password">
            <DataAnnotationsValidator />
            <input type="hidden" name="Input.Email" value="@Input.Email" />
            <input type="hidden" name="Input.Code" value="@Input.Code" />
            <div class="mb-3">
                <label class="mud-input-label">New password</label>
                <InputText type="password" class="mud-input-root mud-input-root-outlined"
                           @bind-Value="Input.Password" autocomplete="new-password"
                           data-pw="true" style="width:100%;padding:8px;" />
                <ValidationMessage For="() => Input.Password" />
            </div>
            <div class="mb-3">
                <label class="mud-input-label">Confirm password</label>
                <InputText type="password" class="mud-input-root mud-input-root-outlined"
                           @bind-Value="Input.ConfirmPassword" autocomplete="new-password"
                           data-pw="true" style="width:100%;padding:8px;" />
                <ValidationMessage For="() => Input.ConfirmPassword" />
            </div>
            <div class="mb-3">
                <label style="cursor:pointer;user-select:none;">
                    <input type="checkbox"
                           onclick="document.querySelectorAll('input[data-pw]').forEach(function(e){e.type=this.checked?'text':'password';}.bind(this));" />
                    Show password
                </label>
            </div>
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary"
                       FullWidth="true" Class="mt-2">Reset password</MudButton>
        </EditForm>
    }
</MudPaper>

@code {
    [SupplyParameterFromQuery] private string? Email { get; set; }
    [SupplyParameterFromQuery(Name = "code")] private string? Code { get; set; }

#pragma warning disable BL0008
    [SupplyParameterFromForm] private ResetInput Input { get; set; } = new();
#pragma warning restore BL0008
    private readonly List<string> _errors = new();

    protected override void OnInitialized()
    {
        // GET: seed from the query. POST: Input.Email/Code already came back via
        // the hidden fields, so ?? leaves them intact.
        Input.Email ??= Email;
        Input.Code ??= Code;
    }

    public async Task ResetUser()
    {
        _errors.Clear();

        var user = await UserManager.FindByEmailAsync(Input.Email!);
        if (user is null)
        {
            _errors.Add("Could not reset the password.");
            return;
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code!));
        }
        catch (FormatException)
        {
            _errors.Add("Invalid password reset link.");
            return;
        }

        var result = await UserManager.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            _errors.AddRange(result.Errors.Select(e => e.Description));
            return;
        }

        RedirectManager.RedirectTo("login");
    }

    private sealed class ResetInput
    {
        public string? Email { get; set; }
        public string? Code { get; set; }

        [Required, StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)] public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeds, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add Components/Account/Pages/ResetPassword.razor
git commit -m "feat: reset-password page (decode token, reset, redirect to login)"
```

---

## Task 3: "Forgot password?" link on the login page

**Files:**
- Modify: `Components/Account/Pages/Login.razor`

**Interfaces:**
- Consumes: route `/forgot-password` from Task 1.

- [ ] **Step 1: Add the link**

In `Components/Account/Pages/Login.razor`, find the trailing "No account?" line:

```razor
    <MudText Typo="Typo.body2" Class="mt-4">
        No account? <MudLink Href="/register">Create one</MudLink>
    </MudText>
```

Replace it with the same line plus a forgot-password link beneath it:

```razor
    <MudText Typo="Typo.body2" Class="mt-4">
        No account? <MudLink Href="/register">Create one</MudLink>
    </MudText>
    <MudText Typo="Typo.body2" Class="mt-1">
        <MudLink Href="/forgot-password">Forgot your password?</MudLink>
    </MudText>
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeds, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add Components/Account/Pages/Login.razor
git commit -m "feat: link to forgot-password from the login page"
```

---

## Task 4: Runtime walkthrough + verification

**Files:** none (verification only).

- [ ] **Step 1: Confirm tests still pass**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: 16/16 passing.

- [ ] **Step 2: Walk the flow**

Run: `dotnet watch` (or `dotnet run`), then:
1. `/login` shows a "Forgot your password?" link → click it → `/forgot-password`.
2. Enter `demo@nook.local` → submit → a "Reset your password" link appears containing `?email=demo%40nook.local&code=<non-empty>`.
3. Click the link → `/reset-password` shows the new-password form. Tick "Show password" → both fields reveal.
4. Enter a new password (e.g. `NewPass123`) in both → submit → redirected to `/login`.
5. Sign in with `demo@nook.local` / `NewPass123` → lands on `/dashboard`. The old password `Demo123!` no longer works.
6. Visit `/reset-password` with no query string → shows "Invalid password reset link." with a link to request a new one.

Expected: all steps behave as described; no unhandled exceptions in the console.

- [ ] **Step 3: (controller note) reset the demo password back if desired**

The walkthrough changes the demo account's password. To restore the seeded `Demo123!`, either reset it again through the flow or drop/reseed the dev database (`dotnet ef database drop -f`, then run — reseeds the demo user). Optional.

---

## Self-review notes

- Covers spec: ForgotPassword (Task 1), ResetPassword with show-password toggle + hidden-field token round-trip (Task 2), login link (Task 3), runtime verification (Task 4). Security note and "no email" behavior are realized by showing the link on-screen.
- No new services/migrations, consistent with the spec's non-goals.
