import { copyFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, type Plugin } from "vite";

const root = dirname(fileURLToPath(import.meta.url));

function flattenHtml(): Plugin {
  return {
    name: "el-flatten-html",
    apply: "build",
    enforce: "post",
    generateBundle(_options, bundle) {
      for (const item of Object.values(bundle)) {
        if (item.type !== "asset" || !item.fileName.endsWith(".html")) continue;
        const slash = item.fileName.lastIndexOf("/");
        if (slash >= 0) {
          item.fileName = item.fileName.slice(slash + 1);
        }
        if (typeof item.source === "string") {
          item.source = item.source
            .replaceAll("../assets/", "assets/")
            .replaceAll(" crossorigin", "");
        }
      }
    },
  };
}

function copyManifest(): Plugin {
  return {
    name: "el-copy-manifest",
    apply: "build",
    closeBundle() {
      copyFileSync(join(root, "manifest.json"), join(root, "dist", "manifest.json"));
    },
  };
}

export default defineConfig({
  root,
  base: "./",
  publicDir: "public",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    modulePreload: false,
    rollupOptions: {
      input: {
        popup: resolve(root, "src/popup.html"),
        options: resolve(root, "src/options.html"),
        app: resolve(root, "src/app.html"),
      },
    },
  },
  plugins: [flattenHtml(), copyManifest()],
});
