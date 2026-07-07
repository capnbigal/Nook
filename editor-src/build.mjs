import { build } from "esbuild";
import { rm, readdir, writeFile } from "node:fs/promises";

const outdir = "../wwwroot/lib/editor";

// clean previous hashed outputs so stale bundles never linger
try {
  for (const f of await readdir(outdir)) {
    if (f.startsWith("nook-editor.") && f.endsWith(".js")) await rm(`${outdir}/${f}`);
  }
} catch { /* dir may not exist yet */ }

const result = await build({
  entryPoints: ["index.mjs"],
  bundle: true,
  format: "esm",
  minify: true,
  sourcemap: false,
  target: ["es2020"],
  outdir,
  entryNames: "nook-editor.[hash]",
  metafile: true,
});

const outFile = Object.keys(result.metafile.outputs)
  .map((p) => p.split("/").pop())
  .find((n) => n.startsWith("nook-editor.") && n.endsWith(".js"));

await writeFile("build-manifest.json", JSON.stringify({ outFile }, null, 2));
console.log("\n==> Bundle written: wwwroot/lib/editor/" + outFile);
console.log("==> Paste this filename into the ImportMap 'nook-editor' entry in Components/App.razor\n");
