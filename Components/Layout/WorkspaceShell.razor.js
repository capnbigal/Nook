// WorkspaceShell.razor.js — collocated keyboard-shortcuts module.
// Global keydown -> .NET. Suppresses single-letter chords while typing in
// inputs/textareas/contenteditable; mod+k always fires regardless of focus.
let _ref = null;
let _handler = null;
let _chordFirst = null;
let _chordTimer = null;

function isEditing(t) {
    if (!t) return false;
    const tag = t.tagName;
    return tag === 'INPUT' || tag === 'TEXTAREA' || t.isContentEditable;
}

export function initialize(dotNetRef) {
    _ref = dotNetRef;
    _handler = (e) => {
        const mod = e.metaKey || e.ctrlKey;
        if (mod && e.key.toLowerCase() === 'k') {
            e.preventDefault();
            _ref.invokeMethodAsync('OnShortcutAsync', 'mod+k');
            return;
        }
        if (isEditing(e.target)) { _chordFirst = null; return; }
        // simple two-key 'g h' style chord
        if (_chordFirst) {
            const combo = `${_chordFirst} ${e.key.toLowerCase()}`;
            _chordFirst = null;
            clearTimeout(_chordTimer);
            _ref.invokeMethodAsync('OnShortcutAsync', combo);
            return;
        }
        if (e.key.toLowerCase() === 'g') {
            _chordFirst = 'g';
            _chordTimer = setTimeout(() => { _chordFirst = null; }, 800);
        }
    };
    document.addEventListener('keydown', _handler);
}

export function dispose() {
    if (_handler) document.removeEventListener('keydown', _handler);
    _handler = null;
    _ref = null;
    _chordFirst = null;
    clearTimeout(_chordTimer);
}
