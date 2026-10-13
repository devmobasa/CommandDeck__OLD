import QtQuick

// Small title-only matcher for the local action menu. Catalog ranking remains
// in the daemon; filtering this short list never requires a protocol round trip.
QtObject {
  function normalized(value) {
    return String(value || "").normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "").toLowerCase()
  }

  function wordScore(word, title) {
    if (word === title) return 1
    const direct = title.indexOf(word)
    if (direct === 0) return 0.94
    if (direct > 0) return /[\s\-_/]/.test(title.charAt(direct - 1)) ? 0.86 : 0.78

    let next = 0
    let first = -1
    let last = -1
    let boundaries = 0
    for (let index = 0; index < title.length && next < word.length; index++) {
      if (title.charAt(index) !== word.charAt(next)) continue
      if (first < 0) first = index
      last = index
      if (index === 0 || /[\s\-_/]/.test(title.charAt(index - 1))) boundaries++
      next++
    }
    if (next !== word.length) return 0
    if (boundaries === word.length) return 0.74
    return 0.4 + 0.25 * word.length / (last - first + 1)
  }

  function filter(actions, query) {
    const needle = normalized(query).trim()
    if (needle.length === 0) return actions.slice()
    const words = needle.split(/\s+/)
    return actions.map((action, index) => {
      const title = normalized(action.title)
      let score = 0
      for (const word of words) {
        const match = wordScore(word, title)
        if (match === 0) return { action: action, index: index, score: 0 }
        score += match
      }
      return { action: action, index: index, score: score / words.length }
    }).filter(entry => entry.score > 0).sort((left, right) => {
      // Keep the menu's result actions and common actions in their own groups.
      const group = Number(left.action.common === true) - Number(right.action.common === true)
      return group || right.score - left.score || left.index - right.index
    }).map(entry => entry.action)
  }
}
