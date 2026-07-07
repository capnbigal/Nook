import * as NookEditor from "nook-editor";

let editor = null;
let dotNetRef = null;
let debounceMs = 800;
let timer = null;
let dirty = false;

export function initialize(el, ref, initialMarkdown, opts) {
    dotNetRef = ref;
    debounceMs = (opts && opts.debounceMs) || 800;

    editor = new NookEditor.Editor({
        element: el,
        extensions: [
            NookEditor.StarterKit,
            NookEditor.TaskList,
            NookEditor.TaskItem.configure({ nested: true }),
            NookEditor.Markdown, // serialize/deserialize markdown
        ],
        content: initialMarkdown, // parsed as markdown by the Markdown extension
        onUpdate: () => {
            dirty = true;
            if (timer) clearTimeout(timer);
            timer = setTimeout(save, debounceMs); // ONE round-trip per idle burst
        },
    });
}

function currentMarkdown() {
    return editor.storage.markdown.getMarkdown();
}

async function save() {
    if (timer) { clearTimeout(timer); timer = null; }
    if (!dirty || !dotNetRef) return;
    dirty = false;
    const md = currentMarkdown();
    await dotNetRef.invokeMethodAsync("SaveBodyAsync", md);
}

// Called from .NET DisposeAsync BEFORE dispose so a pending edit is not lost.
export async function flush() {
    await save();
}

export function dispose() {
    if (timer) clearTimeout(timer);
    timer = null;
    if (editor) { editor.destroy(); editor = null; }
    dotNetRef = null;
}
