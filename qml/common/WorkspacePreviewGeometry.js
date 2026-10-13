.pragma library

function finite(value, bound) {
  return typeof value === "number" && Number.isFinite(value) && Math.abs(value) <= bound
}

function monitorRect(value) {
  if (!value || !finite(value.x, 100000) || !finite(value.y, 100000)
      || !finite(value.width, 32768) || value.width <= 0
      || !finite(value.height, 32768) || value.height <= 0
      || !finite(value.scale, 8) || value.scale < 0.25
      || !Number.isInteger(value.transform) || value.transform < 0 || value.transform > 7) return null
  // IPC monitor extents are pixels; client goal coordinates are logical.
  const swapped = value.transform % 2 === 1
  return { x: value.x, y: value.y,
    width: (swapped ? value.height : value.width) / value.scale,
    height: (swapped ? value.width : value.height) / value.scale }
}

function windowRect(value, monitor) {
  if (!value || !monitor || !Array.isArray(value.at) || !Array.isArray(value.size)
      || value.at.length !== 2 || value.size.length !== 2
      || !finite(value.at[0], 100000) || !finite(value.at[1], 100000)
      || !finite(value.size[0], 32768) || value.size[0] <= 0
      || !finite(value.size[1], 32768) || value.size[1] <= 0) return null
  const x = value.at[0] - monitor.x
  const y = value.at[1] - monitor.y
  const left = Math.max(0, x)
  const top = Math.max(0, y)
  const right = Math.min(monitor.width, x + value.size[0])
  const bottom = Math.min(monitor.height, y + value.size[1])
  if (right <= left || bottom <= top) return null
  return { x: left / monitor.width, y: top / monitor.height,
    width: (right - left) / monitor.width, height: (bottom - top) / monitor.height }
}

function address(value) {
  const text = String(value || "").toLowerCase().replace(/^0x/, "").replace(/^0+/, "")
  return /^[0-9a-f]{1,16}$/.test(text) ? text : ""
}

function order(members) {
  return members.slice(0, 64).sort((a, b) => a.layer - b.layer
    || (a.address < b.address ? -1 : a.address > b.address ? 1 : 0))
}
