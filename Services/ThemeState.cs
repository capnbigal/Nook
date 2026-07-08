using Microsoft.JSInterop;

namespace Nook.Services;

/// <summary>
/// Scoped dark/light state. MudThemeProvider.IsDarkMode binds here; persists to
/// UserPreference and syncs the CSS token layer (&lt;html data-theme&gt;) via the
/// theme-interop.js module.
/// </summary>
public sealed class ThemeState : IAsyncDisposable
{
    private const string ModulePath = "/js/theme-interop.js";

    private readonly IUserPreferenceService _prefs;
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public ThemeState(IUserPreferenceService prefs, IJSRuntime js)
    {
        _prefs = prefs;
        _js = js;
    }

    public bool IsDarkMode { get; private set; }
    public event Action? Changed;

    /// <summary>Loads the persisted dark-mode flag. No JS interop — safe to call from
    /// OnInitializedAsync during prerender/static SSR.</summary>
    public async Task InitializeAsync()
    {
        var pref = await _prefs.GetOrCreateAsync();
        IsDarkMode = pref.IsDarkMode;
        Changed?.Invoke();
    }

    /// <summary>Persists the flag and syncs the CSS token layer via JS. Callers must only invoke
    /// this from an interactive context (e.g. a user-triggered toggle after first render) —
    /// never from OnInitializedAsync, since JS interop is unavailable during prerender.</summary>
    public async Task SetAsync(bool on)
    {
        if (on == IsDarkMode) return;
        IsDarkMode = on;
        await _prefs.SetDarkModeAsync(on);
        await SyncJsAsync(on);
        Changed?.Invoke();
    }

    private async Task SyncJsAsync(bool on)
    {
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("setTheme", on ? "dark" : null);
        }
        catch (JSDisconnectedException)
        {
            // Circuit torn down mid-call; nothing to sync to.
        }
        catch (InvalidOperationException)
        {
            // JS interop not yet available (prerender/static SSR); MudThemeProvider still
            // drives the initial paint and the CSS layer catches up once interactive.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
