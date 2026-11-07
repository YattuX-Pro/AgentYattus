"""Prépare des portraits pour la galerie (deploy/portraits) : retire le liseré clair du détourage et réduit en WebP 560 px.

Usage : python3 scripts/prepare-portraits.py <dossier-des-png> <dossier-de-sortie>   (Pillow requis : pip install pillow)
Chaque PNG donne un WebP du même nom ; nommer les poses <slug>.png et <slug>-parle.png.
"""
import pathlib
import sys

from PIL import Image, ImageFilter

src, dst = map(pathlib.Path, sys.argv[1:3])
dst.mkdir(parents=True, exist_ok=True)
for f in sorted(src.glob("*.png")):
    im = Image.open(f).convert("RGBA")
    r, g, b, a = im.split()
    # Érosion de l'alpha : le bord semi-transparent garde la couleur du fond d'origine (le liseré) ; on le rogne d'un cheveu.
    a = a.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(0.8))
    im = Image.merge("RGBA", (r, g, b, a)).resize((560, 560), Image.LANCZOS)
    out = dst / (f.stem + ".webp")
    im.save(out, "WEBP", quality=88, method=6)
    print(out.name, out.stat().st_size // 1024, "Ko")
