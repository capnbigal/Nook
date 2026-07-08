// theme-interop.js — flips <html data-theme> for the Nook token layer.
export function setTheme(name) {
    const root = document.documentElement;
    if (name) {
        root.dataset.theme = name;
    } else {
        delete root.dataset.theme;
    }
}
