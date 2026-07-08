namespace Nook.Services;

/// <summary>
/// Single source of truth for the vendored TipTap editor bundle URL.
/// <para>
/// <c>EditorHost</c> imports the bundle dynamically by this absolute URL
/// (<c>await import(url)</c>), the same way every other JS module in the app is
/// loaded. We deliberately do NOT use a bare <c>"nook-editor"</c> import
/// specifier + <c>&lt;ImportMap&gt;</c>: Blazor's <c>ImportMap</c> component has no
/// <c>AdditionalImportMapDefinition</c> parameter, so a custom entry is silently
/// dropped and the browser cannot resolve the bare specifier at runtime.
/// </para>
/// <para>
/// When the bundle is rebuilt from <c>/editor-src</c> with a new content hash,
/// update this ONE line to the new filename (see <c>editor-src/build-manifest.json</c>).
/// </para>
/// </summary>
public static class EditorBundle
{
    public const string Url = "/lib/editor/nook-editor.UC55PDUU.js";
}
