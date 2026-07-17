--[[
# viset
version = 1
output = "output/example.png"
browser_arguments = []

[devices.desktop]
mobile = false
touch = false
device_scale = 1.0

[devices.desktop.viewport]
width = 1280
height = 720

[data]
url = "data:text/html;charset=utf-8,%3C!doctype%20html%3E%3Chtml%20lang%3D%22en%22%3E%3Cmeta%20charset%3D%22utf-8%22%3E%3Cmeta%20name%3D%22viewport%22%20content%3D%22width%3Ddevice-width%2Cinitial-scale%3D1%22%3E%3Ctitle%3EViset%3C%2Ftitle%3E%3Cstyle%3Ehtml%2Cbody%7Bwidth%3A100%25%3Bheight%3A100%25%3Bmargin%3A0%7Dbody%7Bdisplay%3Agrid%3Bplace-items%3Acenter%3Bbackground%3A%23131a2a%3Bcolor%3A%23f5f7ff%3Bfont%3A600%2032px%20system-ui%7D%3C%2Fstyle%3E%3Cbody%3EViset%20is%20ready%3C%2Fbody%3E%3C%2Fhtml%3E"
]]

local url = viset.context.data.url
---@cast url string
viset.page.navigate(url)
viset.page.wait_for(viset.javascript [=[
  document.readyState === "complete"
]=], "10s")
viset.snapshot()
