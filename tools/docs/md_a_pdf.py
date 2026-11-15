#!/usr/bin/env python3
"""Convierte un documento Markdown del proyecto a PDF (A4, con número de página).

Uso:   python3 tools/docs/md_a_pdf.py docs/MANUAL_USUARIO.md docs/MANUAL_USUARIO.pdf
Requiere: pip install markdown==3.7  y  Node.js con Playwright/Chromium (imprimir_pdf.js).
"""
import html
import os
import subprocess
import sys
import tempfile

import markdown

CSS = """
@page { size: A4; margin: 18mm 16mm 18mm 16mm; }
body { font-family: 'DejaVu Sans', 'Segoe UI', Arial, sans-serif; font-size: 10pt; line-height: 1.45; color: #1a1a1a; }
h1 { font-size: 20pt; color: #0b3d6b; border-bottom: 2px solid #0b3d6b; padding-bottom: 4px; margin-top: 0; }
h2 { font-size: 14pt; color: #0b3d6b; margin-top: 18pt; border-bottom: 1px solid #c9d6e3; padding-bottom: 2px;
     page-break-after: avoid; }
h3 { font-size: 11.5pt; color: #134f86; margin-top: 12pt; page-break-after: avoid; }
p, li { orphans: 3; widows: 3; }
table { border-collapse: collapse; width: 100%; margin: 6pt 0 10pt 0; font-size: 9pt; page-break-inside: avoid; }
th, td { border: 1px solid #b8c4d0; padding: 3pt 5pt; vertical-align: top; text-align: left; }
th { background: #e8eff6; }
code { font-family: 'DejaVu Sans Mono', Consolas, monospace; font-size: 8.5pt; background: #f2f4f7; padding: 0 2px; }
pre { background: #f2f4f7; border: 1px solid #d5dbe2; padding: 6pt; font-size: 8pt; white-space: pre-wrap;
      word-break: break-all; page-break-inside: avoid; }
pre code { background: none; padding: 0; }
blockquote { border-left: 4px solid #d9822b; background: #fff6ec; margin: 8pt 0; padding: 4pt 10pt; }
hr { border: none; border-top: 1px solid #c9d6e3; margin: 14pt 0; }
"""


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src, dst = sys.argv[1], sys.argv[2]
    text = open(src, encoding="utf-8").read()
    title = text.splitlines()[0].lstrip("# ").strip()
    body = markdown.markdown(text, extensions=["tables", "fenced_code"], output_format="html5")
    page = ("<!doctype html><html lang='es'><head><meta charset='utf-8'><title>%s</title><style>%s</style>"
            "</head><body>%s</body></html>") % (html.escape(title), CSS, body)
    with tempfile.TemporaryDirectory() as tmp:
        h = os.path.join(tmp, "doc.html")
        open(h, "w", encoding="utf-8").write(page)
        js = os.path.join(os.path.dirname(os.path.abspath(__file__)), "imprimir_pdf.js")
        subprocess.run(["node", js, h, os.path.abspath(dst), title], check=True)
    print("PDF escrito en", dst)


if __name__ == "__main__":
    main()
