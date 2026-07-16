--[[
# viset
version = 1
output_root = "output"
output = "screenshots/{device}-{theme}.png"
frame = "builtin:auto"
browser_arguments = ["--hide-scrollbars"]

[devices.laptop]
mobile = false
touch = false
device_scale = 1.0

[devices.laptop.viewport]
width = 1180
height = 720

[devices.phone]
mobile = true
touch = true
device_scale = 1.0

[devices.phone.viewport]
width = 390
height = 844

[matrix]
theme = ["light", "dark"]
]]

local python = os.getenv("VISET_EXAMPLE_PYTHON") or "python3"
local port = os.getenv("VISET_EXAMPLE_PORT") or "41736"
local url = "http://127.0.0.1:" .. port .. "/"
local server = viset.process.start({
  file = python,
  arguments = {
    "-m",
    "http.server",
    port,
    "--bind",
    "127.0.0.1",
    "--directory",
    viset.script.directory .. "/site",
  },
})

local succeeded, failure = pcall(function()
  local theme = viset.context.axes.theme
  local device = viset.context.device
  local render = viset.javascript [=[
    async ({ theme, device }) => {
      document.documentElement.dataset.theme = theme;
      document.documentElement.dataset.device = device;
      window.scrollTo(0, 0);
      document.querySelector(".touch-indicator").style.opacity = "0";
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      return true;
    }
  ]=]

  viset.http.wait({ url = url, timeout = "10s" })
  viset.page.navigate(url)
  viset.page.wait_for("document.readyState === 'complete'", "10s")
  viset.page.evaluate(render, { theme = theme, device = device.name })
  viset.snapshot()
end)

viset.process.stop(server)

if not succeeded then
  error(failure, 0)
end
