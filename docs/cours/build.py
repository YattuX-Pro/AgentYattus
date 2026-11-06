"""Rassemble les leçons de docs/lecons/ en un PDF : docs/cours/agent-yattus-cours.pdf.

    python -m venv .venv && .venv/bin/pip install weasyprint markdown pygments
    .venv/bin/python docs/cours/build.py
"""
import datetime
import html
import re
from pathlib import Path

import markdown
from pygments.formatters import HtmlFormatter
from weasyprint import HTML

ROOT = Path(__file__).resolve().parents[2]
LESSONS = ROOT / "docs" / "lecons"
OUT = Path(__file__).with_name("agent-yattus-cours.pdf")

PHASES = [  # (première leçon, numéro, titre, objectif)
    (1, "0", "Socle et boucle d'agent", "Comprendre ce qu'est un agent en l'écrivant à la main, puis le brancher sur Claude."),
    (8, "1", "Agent Framework", "Refaire la même mission avec Microsoft Agent Framework et fixer l'architecture."),
    (11, "2", "Backend", "Une API modulaire : missions, exécution en arrière-plan, audit, temps réel, approbations."),
    (22, "3", "Client desktop", "L'application Avalonia : suivre une mission en direct et décider."),
    (27, "4", "MVP", "Deux agents qui collaborent, un banc d'évaluation, la sécurité documentée."),
    (30, "5", "Authentification et LLM", "Keycloak, connexion PKCE, clés d'API chiffrées et choix du modèle."),
]


def phase_of(n):
    return [p for p in PHASES if p[0] <= n][-1]


def parse(path):
    text = path.read_text(encoding="utf-8")
    head, _, body = text.partition("\n")
    m = re.match(r"# Leçon (\d+) — (.+)", head)
    num, title = int(m[1]), m[2]
    meta = {}
    lines = body.lstrip("\n").split("\n")
    while lines and lines[0].startswith(">"):
        k = re.match(r">\s*\*\*(\w+)\*\*\s*:\s*(.*)", lines.pop(0))
        if k:
            meta[k[1]] = k[2]
    body = "\n".join(lines)
    # <details> ne s'ouvre pas sur papier : les réponses deviennent un encadré.
    body = re.sub(r"<details>\s*<summary>(.*?)</summary>", r'<div class="answers" markdown="1">\n<p class="answers-t">\1</p>\n', body)
    body = body.replace("</details>", "</div>")
    body = body.replace("⚠️", '<span class="warn">Attention</span>')
    return num, title, meta, body


def md(text):
    return markdown.markdown(text, extensions=["fenced_code", "tables", "codehilite", "md_in_html", "sane_lists"],
                             extension_configs={"codehilite": {"guess_lang": False}})


def inline(text):
    return md(text).removeprefix("<p>").removesuffix("</p>")


lessons = [parse(p) for p in sorted(LESSONS.glob("[0-9][0-9]-*.md"))]

toc, pages = [], []
for num, title, meta, body in lessons:
    ph = phase_of(num)
    if ph[0] == num:
        items = "".join(f'<li><span class="n">{n:02d}</span>{html.escape(t)}</li>'
                        for n, t, _, _ in lessons if phase_of(n) is ph)
        pages.append(f'''<section class="phase-open" id="phase-{ph[1]}">
  <div class="grid"></div>
  <div class="ph-k">Phase</div><div class="ph-n">{ph[1]}</div>
  <h1>{ph[2]}</h1><p class="ph-goal">{ph[3]}</p>
  <ol class="ph-list">{items}</ol></section>''')
        toc.append(f'<li class="toc-ph"><a href="#phase-{ph[1]}">Phase {ph[1]} · {ph[2]}<span class="pg">@@phase-{ph[1]}@@</span></a></li>')
    toc.append(f'<li><a href="#l{num}"><span class="n">{num:02d}</span><span class="t">{inline(title)}</span><span class="pg">@@l{num}@@</span></a></li>')
    pages.append(f'''<article class="lesson" id="l{num}">
  <header><div class="l-k">Leçon {num:02d} · Phase {ph[1]}</div>
  <h1 class="l-title">{inline(title)}</h1>
  <div class="l-meta"><div><b>Commit</b><span>{inline(meta.get("Commit", ""))}</span></div>
  <div><b>Fichiers</b><span>{inline(meta.get("Fichiers", ""))}</span></div></div></header>
  {md(body)}</article>''')

CSS = """
@page{ size:A4; margin:17mm 18mm 18mm 18mm; background:#F6F2E9;
  @top-left{ content:string(lesson); font:500 7.5pt 'IBM Plex Mono',monospace; color:#938E83; }
  @bottom-left{ content:"Agent Yattus · Support de cours"; font:500 7.5pt 'IBM Plex Mono',monospace; color:#938E83; }
  @bottom-right{ content:counter(page); font:500 7.5pt 'IBM Plex Mono',monospace; color:#938E83; } }
@page dark{ margin:0; background:#0E0F11; @top-left{content:none} @bottom-left{content:none} @bottom-right{content:none} }
@page front{ @top-left{content:none} }
:root{ --obs:#0E0F11; --parch:#ECE6D8; --pierre:#A7A196; --cendre:#938E83; --filet:#24272B;
  --vert:#5E9E92; --vertf:#3F7A6F; --laiton:#D8B86E; --laitonf:#A8873A; --cinabre:#D9654B;
  --papier:#F6F2E9; --papier2:#EEE8DB; --encre:#16181B; --encre2:#4A4740; --trait:#DDD5C4; }
*{ box-sizing:border-box; }
body{ margin:0; font-family:'Instrument Sans',sans-serif; color:var(--encre); font-size:9.8pt; line-height:1.55; }
a{ color:inherit; text-decoration:none; }
code{ font:400 8.4pt 'IBM Plex Mono',monospace; background:var(--papier2); padding:.2mm 1.1mm; border-radius:3px; }
.dark, .phase-open{ page:dark; width:210mm; height:297mm; background:var(--obs); color:var(--parch); position:relative; overflow:hidden; padding:28mm 24mm; }
.grid{ position:absolute; inset:0; background-image:radial-gradient(circle,#24272B 1.1px,transparent 1.2px); background-size:7mm 7mm; }
.dark > *:not(.grid), .phase-open > *:not(.grid){ position:relative; }
.glow{ position:absolute !important; width:160mm; height:160mm; right:-50mm; top:35mm; border-radius:50%;
  background:radial-gradient(circle,rgba(94,158,146,.24),rgba(94,158,146,0) 65%); }
.brand{ font-weight:600; letter-spacing:.02em; display:flex; gap:3mm; align-items:center; }
.brand i{ width:9px; height:9px; border-radius:50%; background:var(--vert); display:inline-block; }
.kicker{ margin-top:60mm; font:500 8.5pt 'IBM Plex Mono',monospace; color:var(--vert); letter-spacing:.18em; text-transform:uppercase; }
.dark h1{ font-size:44pt; line-height:1.03; margin:6mm 0 5mm; font-weight:600; letter-spacing:-.02em; }
.dark h1 em{ font-style:normal; color:var(--laiton); }
.lead{ font-size:12pt; color:var(--pierre); max-width:140mm; }
.stats{ display:flex; gap:12mm; margin-top:18mm; }
.stats b{ display:block; font:500 24pt 'IBM Plex Mono',monospace; color:var(--parch); }
.stats span{ font-size:8pt; color:var(--cendre); text-transform:uppercase; letter-spacing:.12em; }
.meta{ position:absolute !important; left:24mm; right:24mm; bottom:24mm; display:flex; justify-content:space-between;
  font:400 8pt 'IBM Plex Mono',monospace; color:var(--cendre); border-top:1px solid var(--filet); padding-top:5mm; }
.front{ page:front; break-before:page; }
.eyebrow{ font:500 7.8pt 'IBM Plex Mono',monospace; letter-spacing:.16em; text-transform:uppercase; color:var(--vertf); }
h2.big{ font-size:24pt; letter-spacing:-.015em; margin:1mm 0 5mm; }
.howto{ display:grid; grid-template-columns:1fr 1fr; gap:4mm; margin-top:6mm; }
.howto div{ background:#fff; border:1px solid var(--trait); border-radius:10px; padding:4mm; }
.howto b{ display:block; margin-bottom:1mm; } .howto p{ margin:0; font-size:9pt; color:var(--encre2); }
.toc{ list-style:none; padding:0; margin:0; columns:2; column-gap:8mm; }
.toc li{ break-inside:avoid; font-size:9pt; padding:1.1mm 0; border-bottom:1px dashed var(--trait); }
.toc li a{ display:flex; gap:2.5mm; }
.toc li a .pg{ margin-left:auto; font:500 8pt 'IBM Plex Mono',monospace; color:var(--cendre); padding-left:2mm; }
.toc .n{ font:500 8pt 'IBM Plex Mono',monospace; color:var(--vertf); flex:none; padding-top:.3mm; }
.toc .toc-ph{ border:0; padding-top:4mm; font:600 8pt 'IBM Plex Mono',monospace; letter-spacing:.1em; text-transform:uppercase; color:var(--encre); }
.toc .toc-ph:first-child{ padding-top:0; }
.toc code{ font-size:7.6pt; }
.phase-open .ph-k{ margin-top:50mm; font:500 9pt 'IBM Plex Mono',monospace; color:var(--vert); letter-spacing:.2em; text-transform:uppercase; }
.phase-open .ph-n{ font:500 96pt/1 'IBM Plex Mono',monospace; color:var(--laiton); margin-top:2mm; }
.phase-open h1{ font-size:32pt; margin:4mm 0 3mm; letter-spacing:-.015em; line-height:1.1; }
.ph-goal{ color:var(--pierre); font-size:11.5pt; max-width:140mm; margin:0 0 12mm; }
.ph-list{ list-style:none; padding:0; margin:0; border-top:1px solid var(--filet); }
.ph-list li{ padding:2.4mm 0; border-bottom:1px solid var(--filet); font-size:10.5pt; display:flex; gap:5mm; }
.ph-list .n{ font:500 9pt 'IBM Plex Mono',monospace; color:var(--vert); padding-top:.6mm; }
.lesson{ break-before:page; }
.lesson header{ border-bottom:1.5px solid var(--encre); padding-bottom:4mm; margin-bottom:5mm; }
.l-k{ font:500 7.8pt 'IBM Plex Mono',monospace; letter-spacing:.16em; text-transform:uppercase; color:var(--vertf); }
.l-title{ string-set:lesson content(text); font-size:21pt; line-height:1.15; letter-spacing:-.015em; margin:2mm 0 4mm; }
.l-title code{ font-size:17pt; background:none; padding:0; }
.l-meta{ font-size:8.6pt; color:var(--encre2); }
.l-meta div{ display:flex; gap:4mm; margin-bottom:1.2mm; }
.l-meta b{ flex:none; width:17mm; font:500 7pt 'IBM Plex Mono',monospace; letter-spacing:.12em; text-transform:uppercase; color:var(--cendre); padding-top:.5mm; }
.l-meta code{ font-size:7.8pt; }
.lesson h2{ font-size:13.5pt; margin:7mm 0 2mm; letter-spacing:-.005em; break-after:avoid; }
.lesson h3{ font-size:11pt; margin:5mm 0 1.5mm; break-after:avoid; }
.lesson h4{ font-size:10pt; margin:4mm 0 1mm; break-after:avoid; }
.lesson p{ margin:0 0 2.6mm; orphans:3; widows:3; }
.lesson ul, .lesson ol{ margin:0 0 3mm; padding-left:5.5mm; }
.lesson li{ margin-bottom:.9mm; } .lesson li::marker{ color:var(--vertf); }
.lesson a{ color:var(--vertf); overflow-wrap:anywhere; }
blockquote{ margin:3mm 0 4mm; padding:3mm 4.5mm; background:#FBF6EA; border-left:3px solid var(--laiton); border-radius:0 8px 8px 0; color:var(--encre2); }
blockquote p:last-child{ margin:0; }
.codehilite{ background:#1B1D21; color:#E6E1D5; border-radius:8px; padding:3mm 4mm; margin:2mm 0 4mm; break-inside:avoid; }
.codehilite pre{ margin:0; font:400 7.9pt/1.5 'IBM Plex Mono',monospace; white-space:pre-wrap; overflow-wrap:anywhere; }
.codehilite code{ background:none; padding:0; font-size:inherit; }
table{ width:100%; border-collapse:collapse; margin:2mm 0 4mm; font-size:8.6pt; break-inside:auto; }
th{ text-align:left; font:500 7pt 'IBM Plex Mono',monospace; letter-spacing:.1em; text-transform:uppercase; color:var(--cendre);
  border-bottom:1.5px solid var(--encre); padding:1.5mm 2.5mm 1.5mm 0; }
td{ border-bottom:1px solid var(--trait); padding:1.6mm 2.5mm 1.6mm 0; vertical-align:top; }
tr{ break-inside:avoid; } td code{ font-size:7.6pt; overflow-wrap:anywhere; }
.warn{ font:600 7pt 'IBM Plex Mono',monospace; letter-spacing:.08em; text-transform:uppercase; color:#fff; background:var(--cinabre);
  padding:.4mm 1.6mm; border-radius:3px; margin-right:1mm; }
.answers{ margin-top:5mm; padding:4mm 5mm; border:1px dashed var(--vert); border-radius:10px; background:rgba(94,158,146,.07); break-inside:avoid; }
.answers-t{ font:500 7.5pt 'IBM Plex Mono',monospace !important; letter-spacing:.14em; text-transform:uppercase; color:var(--vertf); margin-bottom:2mm !important; }
.answers ol{ margin:0; font-size:9pt; }
.end{ break-before:page; }
"""

def pygments_dark():
    return HtmlFormatter(style="monokai").get_style_defs(".codehilite").replace("background: #272822", "background: #1B1D21")

today = datetime.date.today().strftime("%d/%m/%Y")
doc = f"""<!doctype html><html lang="fr"><head><meta charset="utf-8"><title>Agent Yattus — Support de cours</title>
<link href="https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500&display=swap" rel="stylesheet">
<style>{CSS}{pygments_dark()}</style></head><body>
<section class="dark"><div class="grid"></div><div class="glow"></div>
  <div class="brand"><i></i>Agent Yattus</div>
  <div class="kicker">Support de cours</div>
  <h1>Construire une plateforme d'agents IA, <em>commit par commit</em></h1>
  <p class="lead">De la boucle d'agent écrite à la main à une application desktop connectée, sécurisée, où rien de sensible ne se passe sans ton accord. En .NET 10.</p>
  <div class="stats"><div><b>{len(lessons)}</b><span>leçons</span></div><div><b>{len(PHASES)}</b><span>phases</span></div><div><b>.NET 10</b><span>Avalonia · Keycloak</span></div></div>
  <div class="meta"><span>Licence MIT</span><span>Édition du {today}</span></div></section>
<section class="front"><div class="eyebrow">Avant de commencer</div><h2 class="big">Comment lire ce cours</h2>
  <p>Chaque leçon correspond à <b>un commit</b> du dépôt Agent Yattus. Elle explique ce qui a été construit, le problème que cela résout, les options possibles, le choix retenu et pourquoi. Tu peux lire le code de chaque étape avec <code>git show</code> suivi du nom du commit.</p>
  <div class="howto">
    <div><b>Une image</b><p>Les encadrés dorés donnent une comparaison de la vie courante, avant le détail technique.</p></div>
    <div><b>Le code expliqué</b><p>Seuls les extraits qui comptent, commentés ligne à ligne quand c'est utile.</p></div>
    <div><b>Vérifie toi-même</b><p>Une commande ou une manipulation pour voir l'étape fonctionner sur ta machine.</p></div>
    <div><b>Quiz</b><p>Quelques questions à la fin ; les réponses sont dans l'encadré vert, à lire après avoir répondu.</p></div>
  </div></section>
<section class="front"><div class="eyebrow">Sommaire</div><h2 class="big">Les {len(lessons)} leçons</h2><ul class="toc">{''.join(toc)}</ul></section>
{''.join(pages)}
<section class="dark end"><div class="grid"></div><div class="glow"></div>
  <div class="brand"><i></i>Agent Yattus</div><div class="kicker">Et ensuite</div>
  <h1>La suite se construit <em>de la même façon</em></h1>
  <p class="lead">L'application mobile, le déploiement sur un serveur, la reprise des missions, MCP, la mémoire et les compétences des agents. La feuille de route est dans <code style="background:none;color:var(--laiton)">docs/roadmap.md</code>.</p>
  <div class="meta"><span>Leçons sources : docs/lecons</span><span>Licence MIT</span></div></section>
</body></html>"""

# Première passe pour connaître la page de chaque ancre, seconde pour l'écrire dans le sommaire.
anchors = {a: i + 1 for i, page in enumerate(HTML(string=doc, base_url=str(ROOT)).render().pages) for a in page.anchors}
doc = re.sub(r"@@([\w-]+)@@", lambda m: str(anchors[m[1]]), doc)
HTML(string=doc, base_url=str(ROOT)).write_pdf(OUT)
print(OUT)
