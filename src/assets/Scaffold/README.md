# Viset capture project

Edit `capture.lua`, then run:

```sh
viset capture capture.lua
```

Generated output: [`{{OUTPUT_PATH}}`]({{OUTPUT_PATH}})

![Generated Viset capture]({{OUTPUT_PATH}})

Capture files are trusted local Lua code and run with Lua's standard libraries.

## Editor support

`viset init` generated this capture scaffold and the `.luarc.json` and
`.viset/viset.d.lua` LuaLS metadata. The optional editor integrations below only
add highlighting for embedded TOML and JavaScript. They do not execute captures
or replace `viset init`.

- **Neovim 0.12+:** install
  [`getviset/viset.nvim`](https://github.com/getviset/viset.nvim) with your
  plugin manager and the Lua, TOML, and JavaScript Tree-sitter parsers.
- **Emacs 30.1+:** install
  [`getviset/viset.el`](https://github.com/getviset/viset.el) from Git:

  ```elisp
  (package-vc-install
   '(viset-ts-mode :url "https://github.com/getviset/viset.el"))
  ```

  Emacs needs Tree-sitter support and compatible Lua, TOML, and JavaScript
  grammars.
- **VS Code 1.130+:** install the
  [`getviset.viset` v0.1.0 VSIX](https://github.com/getviset/viset-vscode/releases/download/v0.1.0/getviset.viset-0.1.0.vsix)
  from its [GitHub release](https://github.com/getviset/viset-vscode/releases/tag/v0.1.0).
  `getviset.viset` is not currently listed on the Marketplace.
