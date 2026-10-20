.pragma library

// Object key order is irrelevant; input order remains part of the action.
function equal(left, right) {
  if (left === right) return true
  if (left === null || right === null || typeof left !== "object" || typeof right !== "object") return false
  if (Array.isArray(left) !== Array.isArray(right)) return false
  const keys = Object.keys(left).sort()
  const other = Object.keys(right).sort()
  return keys.length === other.length && keys.every((key, index) =>
    key === other[index] && equal(left[key], right[key]))
}

function same(left, right) {
  return !!left && !!right && left.id === right.id && left.kind === right.kind
    && equal(left.arguments || {}, right.arguments || {})
    && equal(left.inputs || [], right.inputs || [])
    && !!left.requiresConfirmation === !!right.requiresConfirmation
    && !!left.closeAfterAction === !!right.closeAfterAction
    && !!left.isDestructive === !!right.isDestructive
}
