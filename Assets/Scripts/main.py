from pathlib import Path
import argparse
import csv
import math
import re
import sys
import matplotlib.pyplot as plt

DEFAULT_DIR = Path.home() / "Desktop" / "UnityLogs"

def find_latest_csv(dir_path: Path) -> Path:
    if not dir_path.exists():
        print(f"[ERROR] La carpeta no existe: {dir_path}")
        sys.exit(1)
    csvs = sorted(dir_path.glob("*.csv"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not csvs:
        print(f"[ERROR] No hay .csv en: {dir_path}")
        sys.exit(1)
    return csvs[0]

def ensure_outdir(base_dir: Path) -> Path:
    out = base_dir / "figs"
    out.mkdir(exist_ok=True)
    return out

def normalize(name: str) -> str:
    return name.strip().lower().replace(" ", "").replace("_", "").replace("-", "")

def pick_index(header, patterns):
    for i, col in enumerate(header):
        cn = normalize(col)
        for pat in patterns:
            if pat.fullmatch(cn):
                return i
    return None

def read_log(csv_path: Path):
    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader, None)
        if not header:
            raise RuntimeError("CSV vacío")

        # Patrones de columna (flexibles)
        re_time  = [re.compile(p) for p in ["t", "time", "timestamp"]]
        re_elaps = [re.compile(p) for p in ["elapsed", "tiempo", "segundos"]]
        re_x     = [re.compile(p) for p in ["x", "posx", "positionx", "position\\.x", "worldx"]]
        re_y     = [re.compile(p) for p in ["y", "posy", "positiony", "position\\.y", "worldy"]]
        re_z     = [re.compile(p) for p in ["z", "posz", "positionz", "position\\.z", "worldz"]]

        idx_t  = pick_index(header, re_time)
        idx_el = pick_index(header, re_elaps)
        idx_x  = pick_index(header, re_x)
        idx_y  = pick_index(header, re_y)
        idx_z  = pick_index(header, re_z)

        if idx_x is None or idx_z is None:
            raise RuntimeError("No se encontraron columnas X y Z (pos_x/pos_z).")
        if idx_el is None and idx_t is None:
            raise RuntimeError("No se encontró 'elapsed' ni 't'.")

        T, EL, X, Y, Z = [], [], [], [], []
        first_t = None

        for row in reader:
            if not row: 
                continue

            def to_f(s):
                try:    return float(str(s).replace(",", "."))
                except: return float("nan")

            t  = to_f(row[idx_t])  if idx_t  is not None and idx_t  < len(row) else float("nan")
            el = to_f(row[idx_el]) if idx_el is not None and idx_el < len(row) else float("nan")
            x  = to_f(row[idx_x])  if idx_x  is not None and idx_x  < len(row) else float("nan")
            y  = to_f(row[idx_y])  if idx_y  is not None and idx_y  < len(row) else float("nan")
            z  = to_f(row[idx_z])  if idx_z  is not None and idx_z  < len(row) else float("nan")

            # Construir 'elapsed' a partir de 't' si no viene
            if idx_el is None:
                if math.isnan(t): 
                    continue
                if first_t is None:
                    first_t = t
                el = t - first_t

            if any(math.isnan(v) for v in [el, x, z]):
                continue

            T.append(t)
            EL.append(el)
            X.append(x)
            Y.append(y)  # puede ser NaN
            Z.append(z)

        if len(EL) < 2:
            raise RuntimeError("Muy pocos datos válidos para graficar.")

        # si Y es todo NaN, márcalo como None
        if all(math.isnan(v) for v in Y):
            Y = None

        return {"elapsed": EL, "x": X, "y": Y, "z": Z}

def plot_line(x, y, title, xlabel, ylabel, out_png: Path):
    plt.figure()
    plt.plot(x, y)
    plt.title(title)
    plt.xlabel(xlabel)
    plt.ylabel(ylabel)
    plt.tight_layout()
    plt.savefig(out_png, dpi=300)
    plt.close()

def plot_path(x, z, title, out_png: Path):
    plt.figure()
    plt.plot(x, z)
    plt.title(title)
    plt.xlabel("X")
    plt.ylabel("Z")
    plt.tight_layout()
    plt.savefig(out_png, dpi=300)
    plt.close()

def compute_speed(elapsed, x, y, z):
    speeds = [float("nan")]
    for i in range(1, len(elapsed)):
        dt = elapsed[i] - elapsed[i-1]
        if dt <= 0:
            speeds.append(float("nan"))
            continue
        dx = x[i] - x[i-1]
        dy = (y[i] - y[i-1]) if y is not None else 0.0
        dz = z[i] - z[i-1]
        dist = math.sqrt(dx*dx + dy*dy + dz*dz)
        speeds.append(dist / dt)
    return speeds

# ----------------- main -----------------
def main():
    ap = argparse.ArgumentParser(description="Graficar el CSV más reciente de una carpeta fija.")
    ap.add_argument("--dir", type=str, help="Carpeta donde están los CSV (por defecto ~/Desktop/UnityLogs).")
    args = ap.parse_args()

    base_dir = Path(args.dir).expanduser() if args.dir else DEFAULT_DIR
    csv_path = find_latest_csv(base_dir)
    print(f"[OK] CSV más reciente: {csv_path}")

    data = read_log(csv_path)
    outdir = ensure_outdir(csv_path.parent)
    print(f"[OK] Guardando PNG en: {outdir}")

    EL, X, Y, Z = data["elapsed"], data["x"], data["y"], data["z"]

    if Y is not None:
        plot_line(EL, Y, "Altura (pos_y) vs tiempo", "Tiempo (s)", "Altura (m)", outdir / "altura_vs_tiempo.png")
        print("[OK] altura_vs_tiempo.png")

    plot_path(X, Z, "Trayectoria X-Z", outdir / "trayectoria_xz.png")
    print("[OK] trayectoria_xz.png")

    speeds = compute_speed(EL, X, Y, Z)
    plot_line(EL, speeds, "Velocidad vs tiempo", "Tiempo (s)", "Velocidad (m/s)", outdir / "velocidad_vs_tiempo.png")
    print("[OK] velocidad_vs_tiempo.png")

    print("\nListo. Carpeta de salida:", outdir)

if __name__ == "__main__":
    main()
