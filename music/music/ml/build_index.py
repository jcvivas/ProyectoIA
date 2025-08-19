# -*- coding: utf-8 -*-
import argparse, os, sys, numpy as np, librosa, tensorflow as tf

# Deben coincidir con los usados para entrenar
SAMPLE_RATE = 22050
N_MELS = 128
N_FFT = 2048
HOP_LENGTH = 1024
MAX_FRAMES = 512

AUDIO_EXTS = {".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".au"}

def preprocess(path):
    y, sr = librosa.load(path, sr=SAMPLE_RATE, duration=30, mono=True)
    mel = librosa.feature.melspectrogram(
        y=y, sr=sr, n_fft=N_FFT, hop_length=HOP_LENGTH, n_mels=N_MELS, power=2.0
    )
    log_mel = librosa.power_to_db(mel, ref=np.max)
    mn, mx = log_mel.min(), log_mel.max()
    log_mel = (log_mel - mn) / (mx - mn + 1e-8)
    T = log_mel.shape[1]
    if T < MAX_FRAMES:
        log_mel = np.pad(log_mel, ((0, 0), (0, MAX_FRAMES - T)), mode="constant")
    else:
        log_mel = log_mel[:, :MAX_FRAMES]
    x = np.expand_dims(log_mel, -1)[None, ...]  # (1,128,512,1)
    return x

def l2_normalize(v, axis=-1, eps=1e-9):
    norm = np.linalg.norm(v, axis=axis, keepdims=True)
    return v / (norm + eps)

def find_embedding_layer(model):
    # 1) nombre exacto
    try:
        return model.get_layer("embedding")
    except Exception:
        pass
    # 2) contiene "embedding"
    for l in model.layers:
        if "embedding" in l.name.lower():
            return l
    # 3) fallback: última capa antes de la softmax si existe
    last = model.layers[-1]
    if hasattr(last, "activation") and getattr(last.activation, "__name__", "") == "softmax":
        # toma la capa previa si es Dense
        for prev in reversed(model.layers[:-1]):
            if prev.__class__.__name__.lower().startswith("dense"):
                return prev
    return None

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, help="Ruta al modelo .h5")
    ap.add_argument("--audio_dir", required=True, help="Carpeta con audios base")
    ap.add_argument("--out", required=True, help="Archivo .npz de salida (embeddings)")
    args = ap.parse_args()

    if not os.path.isdir(args.audio_dir):
        print("Carpeta de audios no existe", file=sys.stderr)
        sys.exit(2)

    model = tf.keras.models.load_model(args.model)

    # Encuentra un audio válido para “construir” el modelo (warm-up)
    first_path = None
    for root, _, files in os.walk(args.audio_dir):
        for fn in files:
            if os.path.splitext(fn)[1].lower() in AUDIO_EXTS:
                first_path = os.path.join(root, fn)
                break
        if first_path:
            break
    if first_path is None:
        print("No se encontraron audios válidos.", file=sys.stderr)
        sys.exit(3)

    x0 = preprocess(first_path)
    _ = model(x0, training=False)  # build: evita 'layer ... has no defined input'

    emb_layer = find_embedding_layer(model)
    if emb_layer is None:
        print("No se encontró capa de embeddings en el modelo.", file=sys.stderr)
        sys.exit(4)

    emb_model = tf.keras.Model(inputs=model.inputs, outputs=emb_layer.output)

    E = []
    titles = []
    artists = []
    covers = []
    processed = 0

    for root, _, files in os.walk(args.audio_dir):
        for fn in files:
            ext = os.path.splitext(fn)[1].lower()
            if ext not in AUDIO_EXTS:
                continue
            path = os.path.join(root, fn)
            try:
                x = preprocess(path)
                v = emb_model.predict(x, verbose=0)[0]
                v = l2_normalize(v)
                E.append(v)
                title = os.path.splitext(fn)[0]
                titles.append(title)
                artists.append("Desconocido")
                covers.append("/img/cover.png")
                processed += 1
                if processed % 20 == 0:
                    print(f"Procesadas {processed} canciones...")
            except Exception as ex:
                print("FAIL", path, ex, file=sys.stderr)

    if not E:
        print("No se generó ningún embedding.", file=sys.stderr)
        sys.exit(5)

    E = np.vstack(E)
    np.savez(args.out, E=E, titles=np.array(titles), artists=np.array(artists), covers=np.array(covers))
    print("Index guardado en:", args.out, " — shape:", E.shape)

if __name__ == "__main__":
    main()
