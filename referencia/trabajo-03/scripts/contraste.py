"""Calcula el contraste WCAG 2.1 de los pares de colores de la consola.

Uso:  python scripts/contraste.py

Los pares son los mismos de docs/brand/README.md. Texto normal: minimo 4,5:1 (AA).
Componentes de interfaz y graficos: minimo 3:1.
"""


def luminancia(hex_color: str) -> float:
    hex_color = hex_color.lstrip("#")
    r, g, b = (int(hex_color[i:i + 2], 16) / 255 for i in (0, 2, 4))

    def lineal(c: float) -> float:
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

    return 0.2126 * lineal(r) + 0.7152 * lineal(g) + 0.0722 * lineal(b)


def contraste(a: str, b: str) -> float:
    la, lb = luminancia(a), luminancia(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


# (descripcion, texto o elemento, fondo, minimo exigido)
PARES = [
    ("Texto principal sobre blanco", "#16211E", "#FFFFFF", 4.5),
    ("Texto principal sobre la página", "#16211E", "#F1F4F3", 4.5),
    ("Texto atenuado sobre blanco", "#566764", "#FFFFFF", 4.5),
    ("Texto atenuado sobre la página", "#566764", "#F1F4F3", 4.5),
    ("Marca sobre blanco (titulares)", "#0F3D3E", "#FFFFFF", 4.5),
    ("Enlace (petróleo fuerte) sobre blanco", "#0F5A73", "#FFFFFF", 4.5),
    ("Petróleo sobre blanco", "#1C7293", "#FFFFFF", 4.5),
    ("Blanco sobre marca (botón primario)", "#FFFFFF", "#0F3D3E", 4.5),
    ("Marca profunda sobre ámbar (botón de acento)", "#0A2C2C", "#F2A93B", 4.5),
    ("Ámbar sobre marca profunda", "#F2A93B", "#0A2C2C", 4.5),
    ("Navegación: texto sobre barra lateral", "#CFE0DE", "#0A2C2C", 4.5),
    ("Navegación: texto apagado sobre barra lateral", "#8FB4B1", "#0A2C2C", 4.5),
    ("Estado ok", "#1E6B3F", "#E7F3EB", 4.5),
    ("Estado error", "#A83226", "#FBEAE8", 4.5),
    ("Estado advertencia", "#7A4B00", "#FFF3DC", 4.5),
    ("Estado información", "#0F5A73", "#E3F1F6", 4.5),
    ("Estado neutro", "#3F4F4C", "#ECEFEE", 4.5),
    ("Placeholder sobre blanco", "#62726F", "#FFFFFF", 4.5),
    ("Aviso: blanco sobre marca profunda", "#FFFFFF", "#0A2C2C", 4.5),
    ("Borde de campo sobre blanco", "#7C8D8A", "#FFFFFF", 3.0),
]

# Este par NO cumple y por eso el ámbar nunca se usa como texto sobre fondo claro.
CONTRAEJEMPLO = ("Ámbar sobre blanco (no usar como texto)", "#F2A93B", "#FFFFFF")

if __name__ == "__main__":
    import sys

    # La terminal de Windows usa otra codificacion por defecto y rompe los acentos.
    sys.stdout.reconfigure(encoding="utf-8")

    fallos = 0
    for descripcion, primero, segundo, minimo in PARES:
        valor = contraste(primero, segundo)
        cumple = valor >= minimo
        fallos += not cumple
        print(f"{valor:6.2f}:1  {'cumple' if cumple else 'FALLA ':6}  (mín. {minimo})  {descripcion}")

    descripcion, primero, segundo = CONTRAEJEMPLO
    print(f"\n{contraste(primero, segundo):6.2f}:1  {descripcion}")
    raise SystemExit(1 if fallos else 0)
