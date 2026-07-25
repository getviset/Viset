--[[
# viset
version = 1
output = "{{OUTPUT_PATH}}"
browser_arguments = []

[devices.desktop]
mobile = false
touch = false
device_scale = 1.0

[devices.desktop.viewport]
width = {{VIEWPORT_WIDTH}}
height = {{VIEWPORT_HEIGHT}}

[data]
url = "{{PAGE_URL}}"
]]

local url = viset.context.data.url
---@cast url string

viset.page.navigate(url)

viset.page.wait_for(viset.javascript [=[
  document.readyState === "complete"
]=], "10s")

viset.snapshot()
