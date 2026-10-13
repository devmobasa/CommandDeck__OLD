.pragma library
.import "Protocol.js" as Protocol

function parse(text, filters, literalPayloads = true) {
  const clauses = []
  const literal = literalPayloads && (/^\s*=/.test(text)
    || /^\s*(codex\s|(?:ai|ask|codex|claude|cursor|grok):)/i.test(text)
    || (filters || []).some(value => value.id === "ai" && !value.excluded))
  const tokens = literal ? [] : text.match(/(?:[^\s"]+|"[^"]*")+/g) || []
  const fields = [Protocol.QueryFieldIds.Title, Protocol.QueryFieldIds.Metadata, Protocol.QueryFieldIds.Path, Protocol.QueryFieldIds.Content, Protocol.QueryFieldIds.Config]
  for (let index = 0; index < tokens.length; index++) {
    let token = tokens[index]
    const excluded = token.length > 1 && token[0] === "-"
    if (excluded) token = token.substring(1)
    let field = null
    const colon = token.indexOf(":")
    if (colon > 0 && fields.indexOf(token.substring(0, colon).toLowerCase()) >= 0) {
      field = token.substring(0, colon).toLowerCase()
      token = token.substring(colon + 1)
    }
    if (token.length >= 2 && token[0] === "\"" && token[token.length - 1] === "\"") token = token.substring(1, token.length - 1)
    if (token.length > 0) clauses.push({field: field, text: token, excluded: excluded})
  }
  const expression = {
    includeFilterIds: (filters || []).filter(value => !value.excluded).map(value => value.id),
    excludeFilterIds: (filters || []).filter(value => value.excluded).map(value => value.id),
    clauses: clauses
  }
  if (literal) expression.literalQuery = text
  return expression
}
