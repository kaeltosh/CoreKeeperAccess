"""Regenerate standalone HTML copies of README/GUIDE (both languages) so
players who only grabbed the release zip (missed them on GitHub) still get
a readable version. Generated files are NOT versioned in git (same
philosophy as the *.preview.html dev previews) - pack-release.ps1 calls this
script right before staging the zip, so they're always fresh at release time.

Usage: py tools/gen_doc_html.py   (run from anywhere, repo root is auto-located
from this script's own path, same pattern as fast-build.ps1)
"""
import markdown
import pathlib

repo = pathlib.Path(__file__).resolve().parent.parent

DOCS = [
    ("README.md", "en", "CoreKeeperAccess"),
    ("README.fr.md", "fr", "CoreKeeperAccess"),
    ("GUIDE.md", "en", "Getting started with Core Keeper"),
    ("GUIDE.fr.md", "fr", "Bien démarrer avec Core Keeper"),
]

TEMPLATE = """<!DOCTYPE html>
<html lang="{lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<style>
body{{max-width:46em;margin:2em auto;padding:0 1em;font-family:Georgia,serif;font-size:1.05em;line-height:1.6}}
h1,h2,h3{{font-family:Arial,sans-serif;line-height:1.25}}
hr{{margin:2em 0}}
code{{background:#eee;padding:0 .2em}}
</style>
</head>
<body>
{body}
</body>
</html>
"""

# Cross-links between the 4 shipped docs point at the .md source in the
# markdown (so they still work when read on GitHub) -> rewritten to the
# sibling .html file for the bundled copies, which link to each other instead.
REWRITES = {
    "README.md": "README.html",
    "README.fr.md": "README.fr.html",
    "GUIDE.md": "GUIDE.html",
    "GUIDE.fr.md": "GUIDE.fr.html",
}

for src_name, lang, title in DOCS:
    src = repo / src_name
    text = src.read_text(encoding="utf-8")
    # Rewrite BEFORE conversion: markdown link syntax "](Foo.md)" is a literal
    # substring in the source; after conversion it becomes an href="..." with
    # no parentheses left to match.
    for md_name, html_name in REWRITES.items():
        text = text.replace(f"]({md_name})", f"]({html_name})")
    html_body = markdown.markdown(text, extensions=["toc", "tables", "fenced_code"])
    out = repo / src_name.replace(".md", ".html")
    out.write_text(TEMPLATE.format(lang=lang, title=title, body=html_body), encoding="utf-8")
    print("wrote", out.name)
