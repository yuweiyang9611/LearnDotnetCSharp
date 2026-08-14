from __future__ import annotations

import argparse
import html
import re
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import quote

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas
from reportlab.platypus import (
    BaseDocTemplate,
    CondPageBreak,
    Flowable,
    Frame,
    HRFlowable,
    LongTable,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents


BODY_FONT = "GuideBody"
BODY_BOLD_FONT = "GuideBodyBold"
CODE_FONT = "GuideCode"

INK = colors.HexColor("#172033")
MUTED = colors.HexColor("#5B6475")
ACCENT = colors.HexColor("#2457A7")
ACCENT_DARK = colors.HexColor("#163A70")
PALE_BLUE = colors.HexColor("#EDF3FC")
PALE_GRAY = colors.HexColor("#F5F7FA")
GRID = colors.HexColor("#CBD3DF")

DEMO_CATEGORIES = {
    "async",
    "collections",
    "compiler",
    "concurrency",
    "crypto",
    "diagnostics",
    "framework",
    "interop",
    "io",
    "language",
    "linq",
    "memory",
    "networking",
    "project",
    "reflection",
    "regex",
    "runtime",
    "serialization",
    "threading",
    "xml",
}


@dataclass(frozen=True)
class Metadata:
    title: str
    subtitle: str
    author: str
    date: str


@dataclass(frozen=True)
class FontFiles:
    regular: Path
    bold: Path
    code: Path | None


class GuideDocTemplate(BaseDocTemplate):
    def __init__(self, filename: str, metadata: Metadata, **kwargs: object) -> None:
        super().__init__(filename, **kwargs)
        self.metadata = metadata
        self._bookmark_sequence = 0

        frame = Frame(
            self.leftMargin,
            self.bottomMargin,
            self.width,
            self.height,
            leftPadding=0,
            rightPadding=0,
            topPadding=7 * mm,
            bottomPadding=4 * mm,
            id="body",
        )
        self.addPageTemplates([PageTemplate(id="guide", frames=[frame])])

    def beforeDocument(self) -> None:  # noqa: N802 - ReportLab API name
        self._bookmark_sequence = 0

    def afterFlowable(self, flowable: Flowable) -> None:  # noqa: N802 - ReportLab API name
        if not isinstance(flowable, Paragraph):
            return

        level_by_style = {
            "Heading1": 0,
            "Heading2": 1,
            "Heading3": 2,
        }
        level = level_by_style.get(flowable.style.name)
        if level is None:
            return

        text = flowable.getPlainText()
        key = f"heading-{self._bookmark_sequence}"
        self._bookmark_sequence += 1
        self.canv.bookmarkPage(key)
        self.canv.addOutlineEntry(text, key, level=level, closed=level > 0)
        self.notify("TOCEntry", (level, text, self.page, key))

    def afterPage(self) -> None:  # noqa: N802 - ReportLab API name
        # BaseDocTemplate invokes afterPage immediately before canvas.showPage().
        # Drawing here prevents split flowables from painting over the header.
        self._draw_page(self.canv, self)

    def _draw_page(self, canvas: Canvas, _: BaseDocTemplate) -> None:
        canvas.saveState()
        canvas.setTitle(self.metadata.title)
        canvas.setAuthor(self.metadata.author)

        canvas.setFont(BODY_FONT, 8)
        canvas.setFillColor(MUTED)
        if self.page > 1:
            canvas.setStrokeColor(GRID)
            canvas.setLineWidth(0.4)
            canvas.line(self.leftMargin, 15 * mm, A4[0] - self.rightMargin, 15 * mm)
            canvas.drawString(self.leftMargin, 10 * mm, self.metadata.title)
            canvas.drawRightString(A4[0] - self.rightMargin, 10 * mm, str(self.page))
        else:
            canvas.drawCentredString(A4[0] / 2, 11 * mm, str(self.page))
        canvas.restoreState()


class MarkdownRenderer:
    _inline_token = re.compile(
        r"(`[^`]+`|\[[^\]]+\]\([^)]+\)|\*\*[^*]+\*\*|(?<!\*)\*[^*]+\*(?!\*))"
    )
    _heading = re.compile(r"^(#{1,3})\s+(.+?)\s*$")
    _unordered = re.compile(r"^(\s*)[-*]\s+(.+)$")
    _ordered = re.compile(r"^(\s*)(\d+)\.\s+(.+)$")
    _table_separator = re.compile(r"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$")

    def __init__(
        self,
        styles: dict[str, ParagraphStyle],
        available_width: float,
        source_path: Path,
        repository_root: Path,
        repository_url: str | None,
    ) -> None:
        self.styles = styles
        self.available_width = available_width
        self.source_path = source_path
        self.repository_root = repository_root.resolve()
        self.repository_url = repository_url.rstrip("/") if repository_url else None
        self._seen_top_level_heading = False

    def render(self, lines: list[str]) -> list[Flowable]:
        story: list[Flowable] = []
        index = 0

        while index < len(lines):
            line = lines[index]

            if not line.strip():
                index += 1
                continue

            if line.startswith("```"):
                code: list[str] = []
                index += 1
                while index < len(lines) and not lines[index].startswith("```"):
                    code.append(lines[index])
                    index += 1
                index += 1
                story.append(self._code_block(code))
                continue

            heading = self._heading.match(line)
            if heading:
                level = len(heading.group(1))
                if level == 1:
                    if self._seen_top_level_heading:
                        story.append(CondPageBreak(58 * mm))
                    self._seen_top_level_heading = True
                story.append(Paragraph(self._inline(heading.group(2)), self.styles[f"Heading{level}"]))
                index += 1
                continue

            if (
                line.lstrip().startswith("|")
                and index + 1 < len(lines)
                and self._table_separator.match(lines[index + 1])
            ):
                table_lines = [line]
                index += 2
                while index < len(lines) and lines[index].lstrip().startswith("|"):
                    table_lines.append(lines[index])
                    index += 1
                story.append(self._table(table_lines))
                story.append(Spacer(1, 4))
                continue

            unordered = self._unordered.match(line)
            if unordered:
                indent = len(unordered.group(1))
                item = unordered.group(2)
                if item.startswith("[ ] "):
                    item = "□ " + item[4:]
                elif item.lower().startswith("[x] "):
                    item = "☑ " + item[4:]
                else:
                    item = "• " + item
                story.append(self._list_paragraph(item, indent))
                index += 1
                continue

            ordered = self._ordered.match(line)
            if ordered:
                indent = len(ordered.group(1))
                item = f"{ordered.group(2)}. {ordered.group(3)}"
                story.append(self._list_paragraph(item, indent))
                index += 1
                continue

            if line.startswith(">"):
                quote_lines: list[str] = []
                while index < len(lines) and lines[index].startswith(">"):
                    quote_lines.append(lines[index][1:].lstrip())
                    index += 1
                story.append(
                    Paragraph(
                        self._inline(" ".join(quote_lines)),
                        self.styles["BlockQuote"],
                    )
                )
                continue

            if line.strip() in {"---", "***"}:
                story.append(HRFlowable(width="100%", thickness=0.5, color=GRID, spaceBefore=6, spaceAfter=8))
                index += 1
                continue

            paragraph_lines = [line.strip()]
            index += 1
            while index < len(lines) and not self._is_special(lines, index):
                paragraph_lines.append(lines[index].strip())
                index += 1
            story.append(Paragraph(self._inline(" ".join(paragraph_lines)), self.styles["Body"]))

        return story

    def _is_special(self, lines: list[str], index: int) -> bool:
        line = lines[index]
        if not line.strip():
            return True
        if line.startswith("```") or line.startswith(">"):
            return True
        if self._heading.match(line) or self._unordered.match(line) or self._ordered.match(line):
            return True
        if line.strip() in {"---", "***"}:
            return True
        return bool(
            line.lstrip().startswith("|")
            and index + 1 < len(lines)
            and self._table_separator.match(lines[index + 1])
        )

    def _list_paragraph(self, item: str, indent: int) -> Paragraph:
        style = self.styles["List"].clone(f"List-{indent}")
        style.leftIndent = self.styles["List"].leftIndent + indent * 3
        return Paragraph(self._inline(item), style)

    def _table(self, lines: list[str]) -> LongTable:
        rows = [self._split_table_row(line) for line in lines]
        column_count = max(len(row) for row in rows)
        normalized = [row + [""] * (column_count - len(row)) for row in rows]

        widths = []
        for column in range(column_count):
            maximum = max(_visual_width(row[column]) for row in normalized)
            widths.append(max(8, min(maximum, 34)))
        total_weight = sum(widths)
        column_widths = [self.available_width * width / total_weight for width in widths]

        data: list[list[Paragraph]] = []
        for row_index, row in enumerate(normalized):
            cells = []
            for cell in row:
                markup = self._inline(cell)
                if row_index == 0:
                    markup = f"<b>{markup}</b>"
                cells.append(Paragraph(markup, self.styles["TableCell"]))
            data.append(cells)

        table = LongTable(
            data,
            colWidths=column_widths,
            repeatRows=1,
            hAlign="LEFT",
            splitByRow=1,
        )
        table.setStyle(
            TableStyle(
                [
                    ("BACKGROUND", (0, 0), (-1, 0), PALE_BLUE),
                    ("TEXTCOLOR", (0, 0), (-1, 0), ACCENT_DARK),
                    ("GRID", (0, 0), (-1, -1), 0.35, GRID),
                    ("VALIGN", (0, 0), (-1, -1), "TOP"),
                    ("LEFTPADDING", (0, 0), (-1, -1), 5),
                    ("RIGHTPADDING", (0, 0), (-1, -1), 5),
                    ("TOPPADDING", (0, 0), (-1, -1), 4),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
                    ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#FAFBFD")]),
                ]
            )
        )
        return table

    @staticmethod
    def _split_table_row(line: str) -> list[str]:
        return [cell.strip() for cell in line.strip().strip("|").split("|")]

    def _code_block(self, lines: list[str]) -> Paragraph:
        wrapped_lines: list[str] = []
        for line in lines or [""]:
            wrapped_lines.extend(_wrap_visual_line(line, 88))

        contains_non_ascii = any(any(ord(character) > 127 for character in line) for line in wrapped_lines)
        font_name = BODY_FONT if contains_non_ascii else CODE_FONT
        markup_lines = []
        for line in wrapped_lines:
            escaped = html.escape(line).replace(" ", "&#160;") or "&#160;"
            markup_lines.append(escaped)
        markup = f'<font name="{font_name}">' + "<br/>".join(markup_lines) + "</font>"
        return Paragraph(markup, self.styles["CodeBlock"])

    def _inline(self, text: str) -> str:
        parts: list[str] = []
        position = 0
        for match in self._inline_token.finditer(text):
            parts.append(html.escape(text[position : match.start()]))
            token = match.group(0)
            if token.startswith("`"):
                value = token[1:-1]
                font_name = CODE_FONT if value.isascii() else BODY_FONT
                parts.append(
                    f'<font name="{font_name}" color="#8A2D3C">{html.escape(value)}</font>'
                )
            elif token.startswith("["):
                link_match = re.match(r"\[([^\]]+)\]\(([^)]+)\)", token)
                if link_match is None:
                    parts.append(html.escape(token))
                else:
                    label, target = link_match.groups()
                    resolved = html.escape(self._resolve_link(target), quote=True)
                    parts.append(
                        f'<link href="{resolved}" color="#2457A7">{html.escape(label)}</link>'
                    )
            elif token.startswith("**"):
                parts.append(f"<b>{html.escape(token[2:-2])}</b>")
            else:
                parts.append(f"<i>{html.escape(token[1:-1])}</i>")
            position = match.end()
        parts.append(html.escape(text[position:]))
        return "".join(parts)

    def _resolve_link(self, target: str) -> str:
        if re.match(r"^(https?://|mailto:|#)", target, flags=re.IGNORECASE):
            return target
        if self.repository_url is None:
            return target

        path_part, separator, fragment = target.partition("#")
        absolute = (self.source_path.parent / path_part).resolve()
        try:
            relative = absolute.relative_to(self.repository_root).as_posix()
        except ValueError:
            return target

        url = f"{self.repository_url}/{quote(relative, safe='/')}"
        if separator:
            url += f"#{quote(fragment)}"
        return url


def _visual_width(text: str) -> int:
    return sum(2 if unicodedata.east_asian_width(character) in {"W", "F"} else 1 for character in text)


def _wrap_visual_line(text: str, maximum_width: int) -> list[str]:
    if not text:
        return [""]

    result: list[str] = []
    remaining = text
    continuation_indent = " " * min(len(text) - len(text.lstrip(" ")) + 2, 12)

    while _visual_width(remaining) > maximum_width:
        width = 0
        split_index = 0
        last_space = -1
        for index, character in enumerate(remaining):
            width += 2 if unicodedata.east_asian_width(character) in {"W", "F"} else 1
            if character.isspace():
                last_space = index
            if width > maximum_width:
                split_index = last_space if last_space > 8 else index
                break
        if split_index <= 0:
            split_index = len(remaining)
        result.append(remaining[:split_index].rstrip())
        remaining = continuation_indent + remaining[split_index:].lstrip()

    result.append(remaining)
    return result


def _read_markdown(path: Path) -> tuple[Metadata, list[str]]:
    content = path.read_text(encoding="utf-8")
    lines = content.splitlines()
    values: dict[str, str] = {}
    body_start = 0

    if lines and lines[0].strip() == "---":
        for index in range(1, len(lines)):
            if lines[index].strip() == "---":
                body_start = index + 1
                break
            match = re.match(r"^([A-Za-z0-9_-]+):\s*(.+?)\s*$", lines[index])
            if match:
                value = match.group(2).strip()
                if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
                    value = value[1:-1]
                values[match.group(1)] = value

    metadata = Metadata(
        title=values.get("title", path.stem),
        subtitle=values.get("subtitle", ""),
        author=values.get("author", "LearnDotnetCSharp"),
        date=values.get("date", ""),
    )
    return metadata, lines[body_start:]


def _register_first_supported_font(
    font_name: str,
    explicit: str | None,
    candidates: list[str],
    label: str,
    *,
    required: bool = True,
) -> Path | None:
    if explicit:
        path = Path(explicit).expanduser().resolve()
        if not path.is_file():
            raise FileNotFoundError(f"{label} font does not exist: {path}")
        paths = [path]
    else:
        paths = [Path(candidate).resolve() for candidate in candidates if Path(candidate).is_file()]

    failures: list[str] = []
    for path in paths:
        try:
            pdfmetrics.registerFont(TTFont(font_name, str(path), subfontIndex=0))
            return path
        except Exception as exception:
            failures.append(f"{path}: {exception}")

    if not required:
        return None

    choices = "\n  - ".join(failures or candidates)
    raise FileNotFoundError(
        f"Could not register a supported {label} font with Chinese glyphs. "
        "Use a TrueType-outline TTF/TTC file and pass its path explicitly.\n"
        f"Checked:\n  - {choices}"
    )


def _register_fonts(args: argparse.Namespace) -> FontFiles:
    regular = _register_first_supported_font(
        BODY_FONT,
        args.font_regular,
        [
            "C:/Windows/Fonts/msyh.ttc",
            "C:/Windows/Fonts/simhei.ttf",
            "/System/Library/Fonts/PingFang.ttc",
            "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        ],
        "regular body",
    )
    bold = _register_first_supported_font(
        BODY_BOLD_FONT,
        args.font_bold,
        [
            "C:/Windows/Fonts/msyhbd.ttc",
            "C:/Windows/Fonts/simhei.ttf",
            "/System/Library/Fonts/PingFang.ttc",
            "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        ],
        "bold body",
    )
    assert regular is not None
    assert bold is not None

    code_candidates = [
        "C:/Windows/Fonts/CascadiaMono.ttf",
        "C:/Windows/Fonts/consola.ttf",
        "/System/Library/Fonts/Menlo.ttc",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
    ]
    code = _register_first_supported_font(
        CODE_FONT,
        args.font_code,
        code_candidates,
        "code",
        required=bool(args.font_code),
    )
    if code is None:
        pdfmetrics.registerFont(TTFont(CODE_FONT, str(regular), subfontIndex=0))
    pdfmetrics.registerFontFamily(
        BODY_FONT,
        normal=BODY_FONT,
        bold=BODY_BOLD_FONT,
        italic=BODY_FONT,
        boldItalic=BODY_BOLD_FONT,
    )
    return FontFiles(regular=regular, bold=bold, code=code)


def _create_styles() -> dict[str, ParagraphStyle]:
    return {
        "Title": ParagraphStyle(
            "Title",
            fontName=BODY_BOLD_FONT,
            fontSize=25,
            leading=35,
            alignment=TA_CENTER,
            textColor=ACCENT_DARK,
            spaceAfter=12,
        ),
        "Subtitle": ParagraphStyle(
            "Subtitle",
            fontName=BODY_FONT,
            fontSize=13,
            leading=21,
            alignment=TA_CENTER,
            textColor=MUTED,
            spaceAfter=14,
        ),
        "TitleMeta": ParagraphStyle(
            "TitleMeta",
            fontName=BODY_FONT,
            fontSize=10,
            leading=17,
            alignment=TA_CENTER,
            textColor=MUTED,
        ),
        "TocTitle": ParagraphStyle(
            "TocTitle",
            fontName=BODY_BOLD_FONT,
            fontSize=20,
            leading=26,
            textColor=ACCENT_DARK,
            spaceAfter=12,
        ),
        "Heading1": ParagraphStyle(
            "Heading1",
            fontName=BODY_BOLD_FONT,
            fontSize=19,
            leading=27,
            textColor=ACCENT_DARK,
            spaceBefore=0,
            spaceAfter=12,
            keepWithNext=True,
        ),
        "Heading2": ParagraphStyle(
            "Heading2",
            fontName=BODY_BOLD_FONT,
            fontSize=14,
            leading=20,
            textColor=ACCENT,
            spaceBefore=10,
            spaceAfter=6,
            keepWithNext=True,
        ),
        "Heading3": ParagraphStyle(
            "Heading3",
            fontName=BODY_BOLD_FONT,
            fontSize=11.5,
            leading=17,
            textColor=INK,
            spaceBefore=8,
            spaceAfter=4,
            keepWithNext=True,
        ),
        "Body": ParagraphStyle(
            "Body",
            fontName=BODY_FONT,
            fontSize=9.5,
            leading=16,
            alignment=TA_JUSTIFY,
            textColor=INK,
            spaceAfter=6,
            wordWrap="CJK",
            splitLongWords=True,
        ),
        "List": ParagraphStyle(
            "List",
            fontName=BODY_FONT,
            fontSize=9.3,
            leading=15.5,
            alignment=TA_LEFT,
            leftIndent=6 * mm,
            firstLineIndent=-4 * mm,
            textColor=INK,
            spaceAfter=2.5,
            wordWrap="CJK",
            splitLongWords=True,
        ),
        "BlockQuote": ParagraphStyle(
            "BlockQuote",
            fontName=BODY_FONT,
            fontSize=9.3,
            leading=15.5,
            leftIndent=7 * mm,
            rightIndent=4 * mm,
            borderColor=ACCENT,
            borderWidth=1.5,
            borderPadding=(5, 7, 5, 8),
            backColor=PALE_BLUE,
            textColor=INK,
            spaceBefore=4,
            spaceAfter=8,
            wordWrap="CJK",
        ),
        "CodeBlock": ParagraphStyle(
            "CodeBlock",
            fontName=CODE_FONT,
            fontSize=8.1,
            leading=11.5,
            leftIndent=0,
            rightIndent=0,
            borderColor=GRID,
            borderWidth=0.5,
            borderPadding=7,
            backColor=PALE_GRAY,
            textColor=colors.HexColor("#263142"),
            spaceBefore=4,
            spaceAfter=8,
            splitLongWords=False,
        ),
        "TableCell": ParagraphStyle(
            "TableCell",
            fontName=BODY_FONT,
            fontSize=8.2,
            leading=12,
            alignment=TA_LEFT,
            textColor=INK,
            wordWrap="CJK",
            splitLongWords=True,
        ),
    }


def _title_and_toc(metadata: Metadata, styles: dict[str, ParagraphStyle]) -> list[Flowable]:
    toc = TableOfContents()
    toc.levelStyles = [
        ParagraphStyle(
            "TocLevel1",
            fontName=BODY_BOLD_FONT,
            fontSize=10,
            leading=15,
            leftIndent=0,
            firstLineIndent=0,
            textColor=ACCENT_DARK,
            spaceBefore=4,
        ),
        ParagraphStyle(
            "TocLevel2",
            fontName=BODY_FONT,
            fontSize=9,
            leading=13,
            leftIndent=7 * mm,
            firstLineIndent=0,
            textColor=INK,
        ),
        ParagraphStyle(
            "TocLevel3",
            fontName=BODY_FONT,
            fontSize=8.2,
            leading=11.5,
            leftIndent=14 * mm,
            firstLineIndent=0,
            textColor=MUTED,
        ),
    ]
    toc.dotsMinLevel = 0

    return [
        Spacer(1, 48 * mm),
        Paragraph(html.escape(metadata.title), styles["Title"]),
        Paragraph(html.escape(metadata.subtitle), styles["Subtitle"]),
        Spacer(1, 15 * mm),
        HRFlowable(width="35%", thickness=1.2, color=ACCENT, spaceAfter=10 * mm),
        Paragraph(html.escape(metadata.author), styles["TitleMeta"]),
        Paragraph(html.escape(metadata.date), styles["TitleMeta"]),
        Spacer(1, 52 * mm),
        Paragraph("基于可运行实验的机制、边界与工程实践", styles["TitleMeta"]),
        PageBreak(),
        Paragraph("目录", styles["TocTitle"]),
        toc,
        PageBreak(),
    ]


def _build_pdf(args: argparse.Namespace) -> tuple[Metadata, FontFiles]:
    source = args.input.resolve()
    output = args.output.resolve()
    repository_root = args.repo_root.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)

    metadata, markdown_lines = _read_markdown(source)
    fonts = _register_fonts(args)
    styles = _create_styles()

    doc = GuideDocTemplate(
        str(output),
        metadata,
        pagesize=A4,
        leftMargin=20 * mm,
        rightMargin=20 * mm,
        topMargin=18 * mm,
        bottomMargin=17 * mm,
        title=metadata.title,
        author=metadata.author,
        allowSplitting=1,
    )
    renderer = MarkdownRenderer(
        styles,
        available_width=doc.width,
        source_path=source,
        repository_root=repository_root,
        repository_url=args.repo_url,
    )
    story = _title_and_toc(metadata, styles)
    story.extend(renderer.render(markdown_lines))
    doc.multiBuild(story)
    return metadata, fonts


def _validate_pdf(args: argparse.Namespace, metadata: Metadata) -> dict[str, int | str]:
    reader = PdfReader(str(args.output.resolve()))
    if reader.is_encrypted:
        raise RuntimeError("Generated PDF is unexpectedly encrypted.")
    if len(reader.pages) < 25:
        raise RuntimeError(f"Generated PDF has too few pages: {len(reader.pages)}")

    width = float(reader.pages[0].mediabox.width)
    height = float(reader.pages[0].mediabox.height)
    if abs(width - A4[0]) > 2 or abs(height - A4[1]) > 2:
        raise RuntimeError(f"Generated PDF is not A4: {width:.1f} x {height:.1f}")

    extracted = "\n".join(page.extract_text() or "" for page in reader.pages)
    required = {metadata.title, "前言", "runtime.overview", "附录 C：从 Markdown 生成 PDF"}
    markdown = args.input.read_text(encoding="utf-8")
    dotted_identifiers = set(re.findall(r"`([a-z][a-z0-9-]*\.[a-z0-9.-]+)`", markdown))
    documented_demo_ids = {
        identifier
        for identifier in dotted_identifiers
        if identifier.partition(".")[0] in DEMO_CATEGORIES
    }
    source_id_pattern = re.compile(r'^\s*"([a-z][a-z0-9-]*\.[a-z0-9.-]+)",\s*$', re.MULTILINE)
    source_demo_ids: set[str] = set()
    source_root = args.repo_root.resolve() / "src"
    for source_path in source_root.rglob("*.cs"):
        source_demo_ids.update(
            identifier
            for identifier in source_id_pattern.findall(source_path.read_text(encoding="utf-8"))
            if identifier.partition(".")[0] in DEMO_CATEGORIES
        )
    if not source_demo_ids:
        raise RuntimeError(f"No demo IDs were discovered under {source_root}.")

    undocumented = sorted(source_demo_ids - documented_demo_ids)
    unknown = sorted(documented_demo_ids - source_demo_ids)
    if undocumented or unknown:
        details: list[str] = []
        if undocumented:
            details.append("missing from study guide: " + ", ".join(undocumented))
        if unknown:
            details.append("not found in source catalog: " + ", ".join(unknown))
        raise RuntimeError("Study guide and source demo IDs differ; " + "; ".join(details))

    required.update(source_demo_ids)
    missing = sorted(marker for marker in required if marker not in extracted)
    if missing:
        raise RuntimeError("Generated PDF text is missing markers: " + ", ".join(missing))
    if "�" in extracted:
        raise RuntimeError("Generated PDF text contains Unicode replacement characters.")
    if not reader.outline:
        raise RuntimeError("Generated PDF does not contain an outline/bookmark tree.")

    return {
        "pages": len(reader.pages),
        "characters": len(extracted),
        "demo_ids": len(source_demo_ids),
        "bytes": args.output.stat().st_size,
    }


def _parse_args() -> argparse.Namespace:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(
        description="Render the LearnDotnetCSharp Markdown study guide as a validated PDF.",
    )
    parser.add_argument(
        "--input",
        type=Path,
        default=root / "docs" / "advanced-dotnet-csharp-study-guide.md",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=root / "output" / "pdf" / "LearnDotnetCSharp-Study-Guide.pdf",
    )
    parser.add_argument("--repo-root", type=Path, default=root)
    parser.add_argument("--repo-url", default=None)
    parser.add_argument("--font-regular", default=None)
    parser.add_argument("--font-bold", default=None)
    parser.add_argument("--font-code", default=None)
    return parser.parse_args()


def main() -> int:
    args = _parse_args()
    try:
        metadata, fonts = _build_pdf(args)
        report = _validate_pdf(args, metadata)
    except Exception as exception:  # The command must return a concise build failure.
        print(f"PDF build failed: {exception}", file=sys.stderr)
        return 1

    print(f"PDF: {args.output.resolve()}")
    print(f"Pages: {report['pages']}")
    print(f"Bytes: {report['bytes']}")
    print(f"Extracted characters: {report['characters']}")
    print(f"Verified demo IDs: {report['demo_ids']}")
    print(f"Body font: {fonts.regular}")
    print(f"Bold font: {fonts.bold}")
    print(f"Code font: {fonts.code or fonts.regular}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
