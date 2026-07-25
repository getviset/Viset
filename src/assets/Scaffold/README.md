# Viset capture project

Edit `capture.lua`, then run:

```sh
viset capture capture.lua
```

Generated output: [`{{OUTPUT_PATH}}`]({{OUTPUT_PATH}})

![Generated Viset capture]({{OUTPUT_PATH}})

Capture files are trusted local Lua code and run with Lua's standard libraries.

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language Server.

Install [`getviset/viset.nvim`](https://github.com/getviset/viset.nvim) with your Neovim plugin manager for TOML header and `viset.javascript` highlighting; no setup call is required.

The plugin requires Neovim 0.12 or newer and the Lua, TOML, and JavaScript Tree-sitter parsers. Run `:checkhealth viset` for diagnostics.
