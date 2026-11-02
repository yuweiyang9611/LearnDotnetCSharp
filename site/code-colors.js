// Colours code after load, so the generated pages, the search index and the PDF stay plain text.
// The token classes come from notebook.css: keyword and number purple, type cyan, call blue,
// string green, comment and meta soft ink. Red is never used in code.
(() => {
  "use strict";

  const csharpKeywords = "abstract and as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false field file finally fixed float for foreach get goto if implicit in init int interface internal is lock long namespace new not null object operator or out override params partial private protected public readonly record ref required return sbyte scoped sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where while with yield".replaceAll(" ", "|");

  const csharp = {
    comment: String.raw`//[^\n]*|/\*[\s\S]*?\*/`,
    string: String.raw`\$?@?"""[\s\S]*?"""|\$?@?"(?:[^"\\\n]|\\.|"")*"|'(?:[^'\\\n]|\\.)+'`,
    meta: String.raw`^[ \t]*#[a-z]+[^\n]*|(?<=^[ \t]*)\[[A-Z]\w*(?:\([^)\n]*\))?\]`,
    number: String.raw`\b\d[\d_]*(?:\.\d+)?[fFdDmMlLuU]*\b`,
    keyword: String.raw`\b(?:${csharpKeywords})\b`,
    call: String.raw`\b[A-Za-z_]\w*(?=(?:<[\w<>, ?]*>)?\()`,
    type: String.raw`\b[A-Z]\w*`,
  };

  // In a shell the program is the type and its switches are the keywords.
  const shell = {
    comment: String.raw`(?<!\S)#[^\n]*`,
    string: String.raw`"[^"\n]*"|'[^'\n]*'`,
    type: String.raw`^[ \t]*(?:[.&][ \t]+)?[A-Za-z_./\\][\w./\\:-]*(?![\w./\\:-]|[ \t]*=)`,
    keyword: String.raw`(?<=\s)--?[A-Za-z][\w-]*`,
    meta: String.raw`\$[A-Za-z_][\w:]*`,
    number: String.raw`(?<![\w.-])\d+(?:\.\d+)?\b`,
  };

  const compile = (rules) => new RegExp(Object.entries(rules).map(([name, source]) => `(?<${name}>${source})`).join("|"), "gm");
  const patterns = { csharp: compile(csharp), powershell: compile(shell), bash: compile(shell) };

  for (const code of document.querySelectorAll('pre code[class*="language-"]')) {
    const pattern = patterns[code.className.match(/language-([\w-]+)/)?.[1]];
    if (!pattern || code.children.length > 0) continue;
    const source = code.textContent;
    const parts = document.createDocumentFragment();
    let position = 0;
    for (const match of source.matchAll(pattern)) {
      if (match[0] === "") continue;
      parts.append(source.slice(position, match.index));
      const token = document.createElement("span");
      token.className = `tok-${Object.keys(match.groups).find((name) => match.groups[name] !== undefined)}`;
      token.textContent = match[0];
      parts.append(token);
      position = match.index + match[0].length;
    }
    parts.append(source.slice(position));
    code.replaceChildren(parts);
  }
})();
