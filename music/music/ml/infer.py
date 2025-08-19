# -*- coding: utf-8 -*-
# infer.py: clasificación de género + recomendaciones por similitud de embeddings (excluye misma pista)

import argparse, json, os, sys
import numpy as np
import librosa
import tensorflow as tf

# ---- Hiperparámetros (usa los mismos que en tu entrenamiento) ----
SAMPLE_RATE = 22050
N_MELS = 128
N_FFT = 2048
HOP_LENGTH = 1024
MAX_FRAMES = 512
DURATION = 30  # segundos

# -------------------- Helpers --------------------
def load_labels(path: str):
    labels = []
    if path and os.path.isfile(path):
        with open(path, "r", encoding="utf-8") as f:
            labels = [ln.strip() for ln in f if ln.strip()]
    return labels  # [] si no existe

def preprocess(audio_path: str):
    y, sr = librosa.load(audio_path, sr=SAMPLE_RATE, duration=DURATION, mono=True)
    mel = librosa.feature.melspectrogram(
        y=y, sr=sr, n_fft=N_FFT, hop_length=HOP_LENGTH, n_mels=N_MELS, power=2.0
    )
    log_mel = librosa.power_to_db(mel, ref=np.max)
    # normalizar [0,1]
    mn, mx = log_mel.min(), log_mel.max()
    log_mel = (log_mel - mn) / (mx - mn + 1e-8)
    # pad/crop
    T = log_mel.shape[1]
    if T < MAX_FRAMES:
        log_mel = np.pad(log_mel, ((0, 0), (0, MAX_FRAMES - T)), mode="constant")
    else:
        log_mel = log_mel[:, :MAX_FRAMES]
    x = np.expand_dims(log_mel, axis=-1)  # (128,512,1)
    x = np.expand_dims(x, axis=0)         # (1,128,512,1)
    return x

def l2_normalize(a: np.ndarray, axis: int = -1, eps: float = 1e-12):
    norm = np.linalg.norm(a, axis=axis, keepdims=True)
    return a / np.maximum(norm, eps)

def stem_from_path(s: str) -> str:
    """Último segmento sin extensión, lower-case, robusto para rutas/urls."""
    if s is None: return ""
    u = str(s).strip().replace("\\", "/")
    leaf = u.split("/")[-1]
    stem, _ = os.path.splitext(leaf)
    return stem.casefold()

def parse_genre_from_title(title: str) -> str:
    """Si el título es p.ej. 'rock.00030' -> 'rock'."""
    t = str(title)
    i = t.find(".")
    if i > 0:
        return t[:i].lower()
    return ""

def safe_get(arr, i, default):
    try:
        if isinstance(arr, np.ndarray):
            return str(arr.tolist()[i])
        return str(arr[i])
    except Exception:
        return default

# -------------------- Main --------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, help="Ruta al modelo .h5")
    ap.add_argument("--audio", required=True, help="Ruta al archivo de audio a clasificar")
    ap.add_argument("--labels", default="", help="labels.txt (orden de entrenamiento)")
    ap.add_argument("--index", default="", help="embeddings.npz con E|embeddings, titles, artists (y opcional covers|paths)")
    ap.add_argument("--topk", type=int, default=8, help="número de recomendaciones a retornar")
    ap.add_argument("--filter-genre", action="store_true",
                    help="si se establece, filtra recomendaciones al género predicho")
    ap.add_argument("--self-threshold", type=float, default=0.999,
                    help="si similitud >= umbral, se asume misma pista y se excluye")
    args = ap.parse_args()

    # Validaciones mínimas
    if not os.path.isfile(args.model):
        print("Modelo .h5 no encontrado", file=sys.stderr); sys.exit(2)
    if not os.path.isfile(args.audio):
        print("Audio no encontrado", file=sys.stderr); sys.exit(2)

    # Cargar labels y modelo
    labels = load_labels(args.labels)
    model = tf.keras.models.load_model(args.model)

    # Predicción de género (softmax)
    x = preprocess(args.audio)
    preds = model.predict(x, verbose=0)
    if preds.ndim == 2:
        preds = preds[0]
    idx = int(np.argmax(preds))
    genero = labels[idx] if labels and idx < len(labels) else f"class_{idx}"

    recomendaciones = []

    # ---- Recomendaciones por embeddings (si hay índice) ----
    if args.index and os.path.isfile(args.index):
        npz = np.load(args.index, allow_pickle=True)

        # Embeddings
        if   "E"          in npz.files: E = npz["E"]
        elif "embeddings" in npz.files: E = npz["embeddings"]
        else:
            print("Index NPZ sin 'E' ni 'embeddings'", file=sys.stderr)
            E = None

        titles  = npz["titles"]  if "titles"  in npz.files else np.array([])
        artists = npz["artists"] if "artists" in npz.files else np.array([])

        if E is not None and len(E) > 0:
            # Asegurar que el modelo esté "conectado" antes de truncarlo
            try:
                _ = model.predict(x, verbose=0)
            except Exception:
                pass

            try:
                emb_layer = model.get_layer("embedding")
                emb_model = tf.keras.Model(inputs=model.input, outputs=emb_layer.output)
            except Exception as e:
                print(f"WARNING: no se pudo obtener la capa 'embedding': {e}", file=sys.stderr)
                emb_model = None

            if emb_model is not None:
                v = emb_model.predict(x, verbose=0)
                if v.ndim == 2:  # (1,D) -> (D,)
                    v = v[0]

                # Normalización L2
                E = np.asarray(E, dtype=np.float32)
                v = np.asarray(v, dtype=np.float32)
                E = l2_normalize(E, axis=1)
                v = l2_normalize(v, axis=0)

                sims = E @ v  # (N,) similitud coseno

                # Ordenar por similitud desc
                order = np.argsort(-sims)

                # Filtrar por género si se pide
                if args.filter_genre:
                    g = genero.lower()
                    order = [i for i in order
                             if parse_genre_from_title(safe_get(titles, i, "")) == g]

                # Excluir la misma pista:
                # 1) comparando el "stem" del query con el "stem" de cada título
                # 2) similitud casi 1 => mismo embedding
                query_stem = stem_from_path(args.audio)
                # Precalcular stems de títulos
                if isinstance(titles, np.ndarray):
                    titles_list = titles.tolist()
                else:
                    titles_list = list(titles)
                stems = [stem_from_path(t) for t in titles_list]

                filtered = []
                for i in order:
                    t_raw  = safe_get(titles,  i, f"track_{i}")
                    t_stem = stems[i] if i < len(stems) else stem_from_path(t_raw)

                    if t_stem == query_stem:
                        continue
                    if sims[i] >= args.self_threshold:
                        continue

                    filtered.append(i)

                take = min(args.topk, len(filtered))
                for i in filtered[:take]:
                    t_raw = safe_get(titles,  i, f"track_{i}")
                    a_raw = safe_get(artists, i, "Desconocido")
                    song_id = stem_from_path(t_raw)  # lo usará el backend para /api/audio/{songId}

                    recomendaciones.append({
                        "titulo":  t_raw,
                        "artista": a_raw,
                        "songId":  t_raw,
                        # "score": float(sims[i])   # <- útil para debug. Si no lo quieres, comenta esta línea.
                    })

    # Salida JSON final
    out = {"genero": genero, "recomendaciones": recomendaciones}
    print(json.dumps(out, ensure_ascii=False))

if __name__ == "__main__":
    main()
