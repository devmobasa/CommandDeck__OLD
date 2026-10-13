.pragma library

// Keeps Tab and Shift+Tab inside a modal surface that lists its controls in
// Tab order. Hidden and disabled controls are skipped, and focus wraps at the
// ends, so it never lands on an unusable control or outside.

// Moves focus to the next (forward) or previous usable control in
// `targets`, wrapping at the ends; focus outside the list starts at an end.
function cycleTargets(targets, forward) {
  const usable = targets.filter(item => item && item.visible && item.enabled)
  if (usable.length === 0) return

  let current = usable.findIndex(item => item.activeFocus)
  if (current < 0) current = forward ? -1 : 0
  const step = forward ? 1 : -1
  usable[(current + step + usable.length) % usable.length].forceActiveFocus()
}
