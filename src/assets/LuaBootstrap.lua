---@class VisetBootstrapApi
---@field api_version 1
---@field context VisetContext
---@field script VisetScript
---@field process VisetProcess
---@field http VisetHttp
---@field page VisetPage
---@field emulation VisetEmulation
---@field snapshot fun()
---@field __duration_ms fun(duration: VisetDuration): number
---@field __now_ms fun(): number
---@field __sleep_ms fun(milliseconds: number)
---@field __recording_create fun()
---@field __recording_start fun()
---@field __recording_stop fun()
---@field __recording_active fun(): boolean

---@type VisetBootstrapApi
local host = assert(rawget(_G, "viset"), "Viset bootstrap host table is missing")

local duration_ms = host.__duration_ms
local now_ms = host.__now_ms
local sleep_ms = host.__sleep_ms
local create_recording = host.__recording_create
local start_recording = host.__recording_start
local stop_recording = host.__recording_stop
local recording_active = host.__recording_active

---@param duration VisetDuration
local function sleep(duration)
  sleep_ms(duration_ms(duration))
end

---@param source string
---@return string
local function javascript(source)
  if type(source) ~= "string" then
    error("viset.javascript requires a string", 2)
  end

  return source
end

---@return VisetRecording
local function record()
  create_recording()

  local recording = {}

  function recording:start()
    start_recording()
  end

  function recording:stop()
    stop_recording()
  end

  ---@param duration VisetDuration
  ---@param callback? fun()
  function recording:during(duration, callback)
    if not recording_active() then
      error("recording:during requires a started recording", 2)
    end

    if callback ~= nil and type(callback) ~= "function" then
      error("recording:during callback must be a function", 2)
    end

    local minimum = duration_ms(duration)
    local started = now_ms()

    if callback ~= nil then
      callback()
    end

    if not recording_active() then
      error("recording:during callback must not stop the recording", 2)
    end

    local remaining = minimum - (now_ms() - started)

    if remaining > 0 then
      sleep_ms(remaining)
    end
  end

  ---@type VisetRecording
  return recording
end

-- Use rawset because the table transitions from VisetBootstrapApi to the
-- public VisetApi shape during bootstrap. This avoids pretending that either
-- lifecycle type contains the fields belonging exclusively to the other.
rawset(host, "sleep", sleep)
rawset(host, "javascript", javascript)
rawset(host, "record", record)
rawset(host, "__duration_ms", nil)
rawset(host, "__now_ms", nil)
rawset(host, "__sleep_ms", nil)
rawset(host, "__recording_create", nil)
rawset(host, "__recording_start", nil)
rawset(host, "__recording_stop", nil)
rawset(host, "__recording_active", nil)
