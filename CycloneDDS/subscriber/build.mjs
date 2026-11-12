import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const base = new URL("./", import.meta.url);
const license = await readFile(new URL("node_modules/three/LICENSE", base), "utf8");
await build({
  absWorkingDir: fileURLToPath(base),
  entryPoints: ["app.js"],
  outfile: ".web-build/viewer.js",
  bundle: true,
  format: "iife",
  minify: true,
  target: "es2022",
  legalComments: "none",
  banner: { js: `/*! Three.js\n${license}*/` },
  loader: { ".png": "dataurl", ".svg": "text", ".fbx": "binary", ".mat": "text" },
});
console.log("Viewer bundle ready. Start the gateway with python bridge.py.");
