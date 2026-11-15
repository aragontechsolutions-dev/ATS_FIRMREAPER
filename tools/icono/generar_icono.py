#!/usr/bin/env python3
"""Genera el icono de FirmReaper (chip de memoria SPI con marca de verificación) en varios tamaños.

Uso:  python3 tools/icono/generar_icono.py
Salida: src/FirmReaper.App/Assets/FirmReaper.ico (16–256 px)
Requiere Pillow.
"""
import os
from PIL import Image, ImageDraw

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SALIDA = os.path.join(RAIZ, "src", "FirmReaper.App", "Assets")
S = 1024  # se dibuja grande y se reduce (bordes suaves)


def dibujar():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    # Fondo: cuadrado redondeado con degradado azul (de arriba abajo).
    fondo = Image.new("RGBA", (S, S))
    fd = ImageDraw.Draw(fondo)
    top, bot = (24, 92, 160), (8, 38, 74)
    for y in range(S):
        t = y / (S - 1)
        fd.line([(0, y), (S, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=200, fill=255)
    im.paste(fondo, (0, 0), mask)

    # Chip: cuerpo oscuro con patas claras a los cuatro lados.
    c0, c1 = 270, S - 270
    pin = (205, 214, 224, 255)
    n, largo, ancho = 5, 95, 46
    paso = (c1 - c0) / n
    for k in range(n):
        m = c0 + paso * (k + 0.5)
        d.rounded_rectangle([m - ancho / 2, c0 - largo, m + ancho / 2, c0 + 10], radius=12, fill=pin)
        d.rounded_rectangle([m - ancho / 2, c1 - 10, m + ancho / 2, c1 + largo], radius=12, fill=pin)
        d.rounded_rectangle([c0 - largo, m - ancho / 2, c0 + 10, m + ancho / 2], radius=12, fill=pin)
        d.rounded_rectangle([c1 - 10, m - ancho / 2, c1 + largo, m + ancho / 2], radius=12, fill=pin)
    d.rounded_rectangle([c0, c0, c1, c1], radius=48, fill=(30, 36, 46, 255), outline=(150, 165, 182, 255), width=14)
    # Muesca del pin 1.
    d.ellipse([c0 + 45, c0 + 45, c0 + 115, c0 + 115], fill=(110, 122, 138, 255))
    # "Pistas" en el chip.
    for k in range(3):
        y = c0 + 190 + k * 70
        d.rounded_rectangle([c0 + 150, y, c1 - 80, y + 26], radius=13, fill=(70, 84, 102, 255))

    # Marca de verificación verde (firmware comprobado) sobre un círculo.
    cx, cy, r = S - 300, S - 300, 205
    d.ellipse([cx - r - 18, cy - r - 18, cx + r + 18, cy + r + 18], fill=(8, 38, 74, 255))
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(46, 170, 90, 255))
    d.line([(cx - 105, cy + 5), (cx - 30, cy + 85), (cx + 115, cy - 85)], fill=(255, 255, 255, 255), width=58, joint="curve")
    return im


def main():
    os.makedirs(SALIDA, exist_ok=True)
    big = dibujar()
    sizes = [256, 128, 64, 48, 32, 24, 16]
    imgs = [big.resize((s, s), Image.LANCZOS) for s in sizes]
    imgs[0].save(os.path.join(SALIDA, "FirmReaper.ico"), format="ICO", sizes=[(s, s) for s in sizes],
                 append_images=imgs[1:])
    print("icono escrito en", SALIDA)


if __name__ == "__main__":
    main()
