# Medium Viset example

This project captures a small dashboard across desktop/phone and light/dark axes. `screenshots.lua` produces four PNGs, while `scroll.lua` produces four continuous WebP recordings. Each file contains its own TOML header and imperative Lua capture.

The desktop recording drives the dashboard from top to bottom and back over one smooth cosine cycle. Phone recordings show a moving touch-contact indicator and complete two cycles. The example records at 40 FPS to make continuous capture and browser-side animation easy to see.

The screenshots use `frame = "builtin:auto"`, which selects Viset's built-in laptop or phone frame from the chosen device. The available selectors are `builtin:auto`, `builtin:phone`, and `builtin:laptop`.

From the Viset repository root, generate all outputs:

```sh
viset capture examples/medium/screenshots.lua --force
viset capture examples/medium/scroll.lua --force
```

Open all four PNGs and four WebPs:

```sh
for file in \
  examples/medium/output/screenshots/desktop-light.png \
  examples/medium/output/screenshots/desktop-dark.png \
  examples/medium/output/screenshots/phone-light.png \
  examples/medium/output/screenshots/phone-dark.png \
  examples/medium/output/animations/desktop-light-scroll.webp \
  examples/medium/output/animations/desktop-dark-scroll.webp \
  examples/medium/output/animations/phone-light-scroll.webp \
  examples/medium/output/animations/phone-dark-scroll.webp
do
  xdg-open "$file"
done
```

Generated captures:

![Desktop light capture](output/screenshots/desktop-light.png)
![Desktop dark capture](output/screenshots/desktop-dark.png)
![Phone light capture](output/screenshots/phone-light.png)
![Phone dark capture](output/screenshots/phone-dark.png)

- [Desktop light sinusoidal scroll recording](output/animations/desktop-light-scroll.webp)
- [Desktop dark sinusoidal scroll recording](output/animations/desktop-dark-scroll.webp)
- [Phone light sinusoidal scroll recording](output/animations/phone-light-scroll.webp)
- [Phone dark sinusoidal scroll recording](output/animations/phone-dark-scroll.webp)

Capture files are trusted local Lua code and run with Lua's standard libraries.
