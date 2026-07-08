// Reads the live text of a contenteditable element. Blazor's @oninput/ChangeEventArgs.Value
// is unreliable for contenteditable innerText, so CommitAsync reads through this instead.
export function readText(el) {
    return el.innerText;
}

// Enter should commit the title, not insert a newline. Blurring re-uses the existing
// @onblur commit path in InlineTitleEditor.razor instead of round-tripping through a
// separate JSInvokable callback.
export function initialize(el) {
    el.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
            e.preventDefault();
            el.blur();
        }
    });
}
