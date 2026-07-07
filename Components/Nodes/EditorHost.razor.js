// The vendored TipTap bundle is loaded by its resolved absolute URL (passed in
// via opts.bundleUrl from EditorBundle.Url) rather than a bare "nook-editor"
// specifier — bare specifiers require an import map, which Blazor's <ImportMap>
// did not emit for a custom entry.
let editor = null;
let dotNetRef = null;
let debounceMs = 800;
let timer = null;
let dirty = false;

// ---- [[wiki-link]] autocomplete state ----
let wikiMenu = null;      // floating dropdown element (in document.body)
let wikiItems = [];       // [{ type:'node'|'create', id?, title, kind? }]
let wikiSelected = 0;
let wikiRange = null;     // { from, to } doc positions covering the `[[query`
let wikiOpen = false;
let wikiSearchTimer = null;
let keydownHandler = null;

export async function initialize(el, ref, initialMarkdown, opts) {
    dotNetRef = ref;
    debounceMs = (opts && opts.debounceMs) || 800;

    const NookEditor = await import(opts.bundleUrl);

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
            scheduleWiki();
        },
        onSelectionUpdate: () => scheduleWiki(),
    });

    // Intercept nav keys BEFORE ProseMirror when the wiki menu is open (capture phase).
    keydownHandler = onWikiKeyDown;
    editor.view.dom.addEventListener("keydown", keydownHandler, true);
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

// ---------------------------------------------------------------------------
// [[wiki-link]] autocomplete
// Detects an open `[[query` before the cursor, queries SearchNodesAsync, and on
// selection inserts plain `[[Title]]`. Node/relation creation happens on save
// (ExtractWikiTitles + ReconcileAsync), so this is purely an insertion aid.
// ---------------------------------------------------------------------------
function detectWiki() {
    if (!editor) return null;
    const sel = editor.state.selection;
    if (!sel.empty) return null;
    const $from = sel.$from;
    const blockStart = $from.start();
    const text = editor.state.doc.textBetween(blockStart, $from.pos, "\n", "￼");
    const idx = text.lastIndexOf("[[");
    if (idx === -1) return null;
    const after = text.slice(idx + 2);
    if (after.includes("]]") || after.includes("\n")) return null; // already closed
    return { query: after, from: blockStart + idx, to: $from.pos };
}

function scheduleWiki() {
    if (!detectWiki()) { closeWiki(); return; }
    if (wikiSearchTimer) clearTimeout(wikiSearchTimer);
    wikiSearchTimer = setTimeout(runWikiSearch, 120);
}

async function runWikiSearch() {
    const trig = detectWiki();
    if (!trig || !dotNetRef) { closeWiki(); return; }
    let results = [];
    try {
        const json = await dotNetRef.invokeMethodAsync("SearchNodesAsync", trig.query);
        results = JSON.parse(json) || [];
    } catch { results = []; }

    // The cursor may have moved during the await — re-detect before showing.
    const still = detectWiki();
    if (!still) { closeWiki(); return; }
    wikiRange = { from: still.from, to: still.to };

    const q = still.query.trim();
    const items = results.map(r => ({ type: "node", id: r.id, title: r.title, kind: r.kind }));
    if (q && !results.some(r => (r.title || "").toLowerCase() === q.toLowerCase())) {
        items.push({ type: "create", title: q });
    }
    if (items.length === 0) { closeWiki(); return; }

    wikiItems = items;
    wikiSelected = 0;
    openWiki();
    paintWiki();
    positionWiki();
}

function onWikiKeyDown(e) {
    if (!wikiOpen || wikiItems.length === 0) return;
    switch (e.key) {
        case "ArrowDown":
            wikiSelected = (wikiSelected + 1) % wikiItems.length; paintWiki();
            e.preventDefault(); e.stopPropagation(); break;
        case "ArrowUp":
            wikiSelected = (wikiSelected - 1 + wikiItems.length) % wikiItems.length; paintWiki();
            e.preventDefault(); e.stopPropagation(); break;
        case "Enter":
        case "Tab":
            chooseWiki(wikiItems[wikiSelected]);
            e.preventDefault(); e.stopPropagation(); break;
        case "Escape":
            closeWiki();
            e.preventDefault(); e.stopPropagation(); break;
    }
}

function chooseWiki(item) {
    if (!item || !wikiRange) { closeWiki(); return; }
    editor.chain().focus().insertContentAt(wikiRange, `[[${item.title}]] `).run();
    closeWiki();
}

function openWiki() {
    if (!wikiMenu) {
        wikiMenu = document.createElement("div");
        wikiMenu.className = "nook-wikimenu";
        // Keep editor focus when clicking a row.
        wikiMenu.addEventListener("mousedown", e => e.preventDefault());
        document.body.appendChild(wikiMenu);
    }
    wikiOpen = true;
    wikiMenu.style.display = "block";
}

function closeWiki() {
    wikiOpen = false;
    wikiItems = [];
    wikiRange = null;
    if (wikiSearchTimer) { clearTimeout(wikiSearchTimer); wikiSearchTimer = null; }
    if (wikiMenu) wikiMenu.style.display = "none";
}

function paintWiki() {
    if (!wikiMenu) return;
    wikiMenu.innerHTML = "";
    wikiItems.forEach((it, i) => {
        const row = document.createElement("div");
        row.className = "nook-wikimenu__row" + (i === wikiSelected ? " is-active" : "");
        row.textContent = it.type === "create" ? `Create "${it.title}"` : it.title;
        row.addEventListener("mousedown", e => { e.preventDefault(); chooseWiki(it); });
        wikiMenu.appendChild(row);
    });
}

function positionWiki() {
    if (!wikiMenu || !wikiRange) return;
    const coords = editor.view.coordsAtPos(wikiRange.to);
    wikiMenu.style.left = Math.round(coords.left) + "px";
    wikiMenu.style.top = Math.round(coords.bottom + 4) + "px";
}

export function dispose() {
    if (timer) clearTimeout(timer);
    timer = null;
    if (wikiSearchTimer) { clearTimeout(wikiSearchTimer); wikiSearchTimer = null; }
    if (editor && keydownHandler) editor.view.dom.removeEventListener("keydown", keydownHandler, true);
    keydownHandler = null;
    if (wikiMenu) { wikiMenu.remove(); wikiMenu = null; }
    wikiOpen = false; wikiItems = []; wikiRange = null;
    if (editor) { editor.destroy(); editor = null; }
    dotNetRef = null;
}
