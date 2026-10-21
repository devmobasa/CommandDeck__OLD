.pragma library
.import "QuicklinkRules.js" as Rules

function validate(link, entries) {
  if (!link.title.trim() || link.title.length > 64 || /[\x00-\x1f\x7f-\x9f]/.test(link.title))
    return Rules.titleMessage
  if (!link.urlTemplate.trim() || link.urlTemplate.length > 2048 || /[\x00-\x1f\x7f-\x9f]/.test(link.urlTemplate))
    return Rules.urlLengthMessage
  const url = link.urlTemplate.replace(/\{query\}/gi, "sample")
  const address = /^https?:\/\/(\[[^\]\s\/@{}]+\]|[^\s\/@{}:?#]+)(?::([0-9]*))?(?:[/?#][^{}]*)?$/i.exec(url)
  if (!address || (address[2] && Number(address[2]) > 65535)
      || (address[1][0] === "[" && !isIpv6(address[1])))
    return Rules.urlMessage
  if (link.alias) {
    const alias = link.alias.trim()
    if (!new RegExp(Rules.aliasPattern).test(alias)) return Rules.aliasMessage
    if (Rules.reservedAliases.indexOf(alias.toLowerCase()) >= 0)
      return Rules.reservedAliasMessage.replace("{0}", alias)
    if (alias !== link.alias) return Rules.aliasMessage
  }
  if (link.icon && (link.icon.length > 256 || /[\x00-\x1f\x7f-\x9f]/.test(link.icon)))
    return Rules.iconMessage
  for (const entry of entries || []) {
    if (entry.quicklink.id === link.id) continue
    if (link.alias && entry.quicklink.alias
        && link.alias.toLowerCase() === entry.quicklink.alias.toLowerCase())
      return "Quicklink alias '" + link.alias + "' is configured more than once."
  }
  return ""
}

function isIpv6(host) {
  let address = host.slice(1, -1).split("%")[0]
  if (address.indexOf(".") >= 0) {
    const tail = address.slice(address.lastIndexOf(":") + 1).split(".")
    if (tail.length !== 4 || tail.some(part => !/^[0-9]{1,3}$/.test(part) || Number(part) > 255)) return false
    address = address.slice(0, address.lastIndexOf(":") + 1) + "0:0"
  }
  const compressed = address.split("::")
  if (compressed.length > 2) return false
  const groups = compressed.map(part => part ? part.split(":") : []).reduce((all, part) => all.concat(part), [])
  if (groups.some(group => !/^[0-9a-f]{1,4}$/i.test(group))) return false
  return compressed.length === 2 ? groups.length < 8 : groups.length === 8
}

function preview(template, query) {
  return template.replace(/\{query\}/gi, encodeURIComponent(query).replace(/[!'()*]/g,
    character => "%" + character.charCodeAt(0).toString(16).toUpperCase()))
}
