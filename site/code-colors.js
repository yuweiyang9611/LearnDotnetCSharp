// Colours code after load, so the generated pages, the search index and the PDF stay plain text.
// The token classes come from notebook.css: keyword and number purple, type cyan, call blue,
// string green, comment and meta soft ink. Red is never used in code.
// window.CodeColors colours text on demand for the source panel, which loads files after the page.
(() => {
  "use strict";

  const words = (list) => list.trim().split(/\s+/).join("|");

  const csharpKeywords = words("abstract and as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false field file finally fixed float for foreach get goto if implicit in init int interface internal is lock long namespace new not null object operator or out override params partial private protected public readonly record ref required return sbyte scoped sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where while with yield");

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

  // C and C++ as the native interop sources use them: preprocessor lines are meta, *_t and PascalCase names are types.
  const cKeywords = words("alignas alignof auto bool break case catch char class const constexpr const_cast continue decltype default delete do double dynamic_cast else enum explicit export extern false final float for friend goto if inline int long mutable namespace new noexcept nullptr operator override private protected public register reinterpret_cast restrict return short signed sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union unsigned using virtual void volatile wchar_t while NULL");
  const c = {
    comment: String.raw`//[^\n]*|/\*[\s\S]*?\*/`,
    string: String.raw`"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)+'|(?<=^[ \t]*#[ \t]*include[ \t]*)<[^>\n]*>`,
    meta: String.raw`^[ \t]*#[ \t]*[a-z]+[^\n]*`,
    number: String.raw`\b(?:0[xX][\dA-Fa-f']+|\d[\d']*(?:\.\d+)?(?:[eE][+-]?\d+)?)[uUlLfF]*\b`,
    keyword: String.raw`\b(?:${cKeywords})\b`,
    call: String.raw`\b[A-Za-z_]\w*(?=\s*\()`,
    type: String.raw`\b(?:[A-Z]\w*|\w+_t|std::\w+)\b`,
  };

  const pythonKeywords = words("False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield self");
  const python = {
    comment: String.raw`#[^\n]*`,
    string: String.raw`[rRbBfFuU]{0,2}(?:"""[\s\S]*?"""|'''[\s\S]*?'''|"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*')`,
    meta: String.raw`^[ \t]*@\w[\w.]*`,
    number: String.raw`\b(?:0[xX][\dA-Fa-f_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?j?)\b`,
    keyword: String.raw`\b(?:${pythonKeywords})\b`,
    call: String.raw`\b[A-Za-z_]\w*(?=\s*\()`,
    type: String.raw`\b[A-Z]\w*`,
  };

  const fsharpKeywords = words("abstract and as assert base begin class default delegate do done downcast downto elif else end exception extern false finally fixed for fun function global if in inherit inline interface internal lazy let match member module mutable namespace new not null of open or override private public rec return select sig static struct then to true try type upcast use val void when while with yield");
  const fsharp = {
    comment: String.raw`//[^\n]*|\(\*[\s\S]*?\*\)`,
    string: String.raw`\$?@?"""[\s\S]*?"""|\$?@?"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)'`,
    meta: String.raw`\[<[^\]\n]*>\]|^[ \t]*#[a-z]+[^\n]*`,
    number: String.raw`\b\d[\d_]*(?:\.\d+)?[fFmMuUlLysn]*\b`,
    keyword: String.raw`\b(?:${fsharpKeywords})\b`,
    call: String.raw`\b[A-Za-z_]\w*(?=\s*\()`,
    type: String.raw`\b[A-Z]\w*`,
  };

  const vbKeywords = words("AddHandler AddressOf Alias And AndAlso As Async Await Boolean ByRef Byte ByVal Call Case Catch Char Class Const Continue Date Decimal Declare Default Delegate Dim DirectCast Do Double Each Else ElseIf End Enum Erase Error Event Exit False Finally For Friend Function Get GetType Global GoTo Handles If Implements Imports In Inherits Integer Interface Is IsNot Iterator Let Lib Like Long Loop Me Mod Module MustInherit MustOverride MyBase MyClass Namespace Narrowing New Next Not Nothing NotInheritable NotOverridable Object Of On Operator Option Optional Or OrElse Overloads Overridable Overrides ParamArray Partial Private Property Protected Public RaiseEvent ReadOnly ReDim RemoveHandler Resume Return Select Set Shadows Shared Short Single Static Step Stop String Structure Sub SyncLock Then Throw To True Try TryCast TypeOf UInteger ULong UShort Using When While Widening With WithEvents WriteOnly Xor Yield");
  const vb = {
    comment: String.raw`'[^\n]*`,
    string: String.raw`\$?"(?:[^"\n]|"")*"`,
    meta: String.raw`^[ \t]*#[A-Za-z]+[^\n]*|(?<=^[ \t]*)<[A-Z]\w*(?:\([^)\n]*\))?>`,
    number: String.raw`\b\d[\d_]*(?:\.\d+)?[A-Za-z]*\b`,
    keyword: String.raw`\b(?:${vbKeywords})\b`,
    call: String.raw`\b[A-Za-z_]\w*(?=\()`,
    type: String.raw`\b[A-Z]\w*`,
  };

  // Data files: keys as types, literals as strings and numbers.
  const data = {
    comment: String.raw`//[^\n]*|/\*[\s\S]*?\*/|(?<!\S)#[^\n]*`,
    type: String.raw`"(?:[^"\\\n]|\\.)*"(?=\s*:)|^[ \t]*(?:-[ \t]+)?[\w.-]+(?=:(?:[ \t]|$))`,
    string: String.raw`"(?:[^"\\\n]|\\.)*"|'[^'\n]*'`,
    number: String.raw`(?<![\w.-])-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b`,
    keyword: String.raw`\b(?:true|false|null)\b`,
  };

  // XML and the project files: tags are types, attribute names calls, attribute values strings.
  const xml = {
    comment: String.raw`<!--[\s\S]*?-->`,
    meta: String.raw`<\?[\s\S]*?\?>|<!\[CDATA\[[\s\S]*?\]\]>|<!DOCTYPE[^>]*>`,
    string: String.raw`"[^"\n]*"|'[^'\n]*'`,
    type: String.raw`(?<=</?)[A-Za-z_][\w.:-]*`,
    call: String.raw`\b[A-Za-z_][\w.:-]*(?=\s*=)`,
  };

  const compile = (rules) => new RegExp(Object.entries(rules).map(([name, source]) => `(?<${name}>${source})`).join("|"), "gm");
  const patterns = {
    bash: compile(shell),
    c: compile(c),
    cpp: compile(c),
    csharp: compile(csharp),
    fsharp: compile(fsharp),
    json: compile(data),
    powershell: compile(shell),
    python: compile(python),
    vb: compile(vb),
    xml: compile(xml),
    yaml: compile(data),
  };
  const languages = {
    bat: "bash", c: "c", cmd: "bash", cpp: "cpp", cs: "csharp", csproj: "xml", fs: "fsharp", fsproj: "xml", h: "c", hpp: "cpp",
    json: "json", props: "xml", ps1: "powershell", psd1: "powershell", py: "python", resx: "xml", sh: "bash", slnx: "xml",
    targets: "xml", vb: "vb", vbproj: "xml", xml: "xml", yaml: "yaml", yml: "yaml",
  };

  function highlight(source, language) {
    const parts = document.createDocumentFragment();
    const pattern = patterns[language];
    if (!pattern) {
      parts.append(source);
      return parts;
    }
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
    return parts;
  }

  function languageFor(path) {
    const extension = String(path).toLowerCase().match(/\.([a-z0-9]+)$/)?.[1] ?? "";
    return languages[extension] ?? (extension || "text");
  }

  window.CodeColors = Object.freeze({ highlight, languageFor });

  function colourPage() {
    for (const code of document.querySelectorAll('pre code[class*="language-"]')) {
      const language = code.className.match(/language-([\w-]+)/)?.[1];
      if (!patterns[language] || code.children.length > 0) continue;
      code.replaceChildren(highlight(code.textContent, language));
    }
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", colourPage);
  else colourPage();
})();
