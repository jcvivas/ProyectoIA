# ProyectoIAAquí tienes un **README** listo para tu repo. Cópialo como `README.md` en la raíz del proyecto.

---

# MelodIA (Blazor Server + Python/TensorFlow)

Clasificador de género musical con recomendaciones por similitud.
Frontend en **Blazor Server (.NET 8)**, inferencia en **Python (TensorFlow)** y conversión de audio con **ffmpeg**.

## ✨ Funcionalidades

* Subida de audio desde el navegador y/o **Song ID** de tu librería local.
* Predicción de **género** con un modelo `.h5`.
* **Recomendaciones** por similitud (embeddings) usando un índice `embeddings.npz`.
* Reproducción de previews de tu librería: `/api/audio/{songId}` (convierte `.au` a WAV al vuelo).
* Feedback de usuario (like/dislike global y por pista) y **/estadisticas** con gráficos SVG.

---

## 📦 Requisitos

### Sistema

* **Windows 10/11** (probado en Windows; Linux/Mac también posible con cambios de rutas).
* **.NET SDK 8.0**
  Verifica: `dotnet --version` (debe empezar con 8.)
* **Python 3.11 o 3.12** (recomendado usar **venv** local del proyecto)
* **ffmpeg** instalado y en el `PATH`

### Python (dentro del venv)

Paquetes mínimos:

* `tensorflow==2.17.*` (o tu versión compatible en Windows)
* `librosa`
* `numpy`
* `scipy`
* `soundfile`
* `scikit-learn`

> Nota: en Windows, TensorFlow x86\_64 requiere Visual C++ Redistributable (normalmente ya instalado por VS).

---

## 🛠️ Preparación del entorno

### 1) Clonar y abrir

```powershell
git clone <tu-repo>
cd music\music   # entra al proyecto .NET
```

Abre la solución en **Visual Studio 2022** o usa `dotnet run`.

### 2) Crear el entorno Python (venv)

```powershell
# en la carpeta del proyecto .NET (music\music)
python -m venv .venv
.\.venv\Scripts\activate

# Actualiza pip y instala deps
python -m pip install --upgrade pip
pip install tensorflow==2.17.0 librosa numpy scipy soundfile scikit-learn
```

> **Importante:** en Windows evita el Python de Microsoft Store.
> Si ves el error *“no se encontró Python; ejecutar sin argumentos para instalar desde Microsoft Store”*, apunta a tu venv:
> `C:\...\music\music\.venv\Scripts\python.exe`.

### 3) Instalar ffmpeg

* Con **winget**:

```powershell
winget install Gyan.FFmpeg
```

* o con **choco**:

```powershell
choco install ffmpeg
```

Verifica:

```powershell
ffmpeg -version
```

### 4) Archivos del modelo (carpeta `ml\`)

Coloca en `music\music\ml\`:

* `modelo.h5` – tu modelo Keras.
* `labels.txt` – 1 género por línea, en el **mismo orden** de entrenamiento.
* `infer.py` – script de inferencia (ya incluido en el repo).
* `build_index.py` – script para generar `embeddings.npz` (ya incluido).
* (opcional) `embeddings.npz` – índice de embeddings para recomendaciones.

### 5) Librería de audios y temp

Crea estas carpetas (o usa otras, pero actualiza rutas):

* `C:\ia\audios` → tus audios (`.mp3, .wav, .flac, .ogg, .m4a, .aac, .wma, .au`)
* `C:\ia\temp`   → temporales (subidas/convertidos)

---

## ⚙️ Configuración (`appsettings.json`)

Edita `music\music\appsettings.json`:

```json
{
  "Api": {
    "BaseUrl": "https://localhost:7165/api"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Inference": {
    "PythonExe": "C:\\ruta\\a\\tu\\repo\\music\\music\\.venv\\Scripts\\python.exe",
    "ScriptPath": "C:\\ruta\\a\\tu\\repo\\music\\music\\ml\\infer.py",
    "ModelPath": "C:\\ruta\\a\\tu\\repo\\music\\music\\ml\\modelo.h5",
    "LabelsPath": "C:\\ruta\\a\\tu\\repo\\music\\music\\ml\\labels.txt",
    "EmbeddingsIndexPath": "C:\\ruta\\a\\tu\\repo\\music\\music\\ml\\embeddings.npz",
    "SongLibraryDir": "C:\\ia\\audios",
    "TempDir": "C:\\ia\\temp",
    "TimeoutSeconds": 60
  }
}
```

* `PythonExe`: apunta **a tu venv** (`.venv\Scripts\python.exe`).
* Si **no** tienes `embeddings.npz`, déjalo vacío o quita la clave (habrá predicción de género pero recomendaciones vacías).

---

## 📚 Construir el índice de embeddings (opcional pero recomendado)

Genera `embeddings.npz` a partir de tu librería local (usa el **mismo** preprocesado que en el entrenamiento):

```powershell
# Activa venv si aún no:
.\.venv\Scripts\activate

# Desde music\music
python .\ml\build_index.py ^
  --model .\ml\modelo.h5 ^
  --labels .\ml\labels.txt ^
  --library "C:\ia\audios" ^
  --out .\ml\embeddings.npz ^
  --n-workers 2
```

Con esto, las recomendaciones por similitud **sí** se poblarán.

---

## ▶️ Levantar la app

Con Visual Studio (**F5**) o por consola:

```powershell
# en music\music
dotnet restore
dotnet run
```

La app mostrará algo como:

```
Now listening on: https://localhost:7165
Now listening on: http://localhost:5241
```

Abre:

* **Frontend:** `https://localhost:7165/`
* **Recomendar:** `https://localhost:7165/recomendar`
* **Estadísticas:** `https://localhost:7165/estadisticas`

---

## 🎧 Tipos de audio soportados

* **Subida desde navegador:** `audio/*` (probados `.mp3, .wav, .flac, .ogg, .m4a, .aac, .wma, .au`)
  Tamaño recomendado `< 50 MB` (el backend rechaza > 100 MB por defecto).
* **Librería local (`SongLibraryDir`)**: mismos formatos que arriba.
  El endpoint de preview `/api/audio/{songId}`:

  * Busca `songId` con extensiones conocidas.
  * Si el archivo es `.au`, lo **convierte al vuelo** a WAV con ffmpeg y lo sirve como streaming.

> `songId` se resuelve por nombre de archivo **sin extensión**.
> Ejemplo: `C:\ia\audios\rock.00030.au` → `/api/audio/rock.00030`

---

## 🔬 Probar inferencia por consola (debug rápido)

```powershell
.\.venv\Scripts\activate
python .\ml\infer.py --model .\ml\modelo.h5 --audio "C:\ia\audios\blues.00000.au" --labels .\ml\labels.txt --index .\ml\embeddings.npz --topk 8
```

Salida esperada (ejemplo):

```json
{"genero":"rock","recomendaciones":[{"songId":"blues.00000","titulo":"blues.00000","artista":"Desconocido"},{"songId":"rock.00030","titulo":"rock.00030","artista":"Desconocido"}]}
```

---

## 🧩 Estructura relevante

```
music/
└─ music/                  # proyecto .NET
   ├─ ml/
   │  ├─ infer.py
   │  ├─ build_index.py
   │  ├─ modelo.h5
   │  ├─ labels.txt
   │  └─ embeddings.npz    # (opcional si no generaste aún)
   ├─ wwwroot/
   │  ├─ bootstrap/...
   │  └─ app.css
   ├─ Pages/
   │  ├─ Recomendar.razor
   │  └─ Estadisticas.razor
   ├─ Components/
   │  └─ Layout/MainLayout.razor
   ├─ appsettings.json
   └─ Program.cs
```

---

## 🧯 Problemas comunes (y cómo resolverlos)

* **HTTP 500 con detalle “exit 9009 / no se encontró Python”**
  → El `PythonExe` apunta al alias de la Microsoft Store.
  **Solución:** usa el de tu venv: `...\.venv\Scripts\python.exe`.

* **`UnicodeEncodeError cp1252` al imprimir JSON**
  → Consola Windows no UTF-8.
  **Soluciones:**

  * Asegúrate que `infer.py` inicia con `# -*- coding: utf-8 -*-`.
  * (Opcional) `setx PYTHONIOENCODING utf-8` y reinicia la consola.

* **El cliente reintenta POST y falla con “stream consumed”**
  → No reintentes `multipart/form-data`. Si tienes un `RetryHandler`, no lo apliques a la subida de archivo o duplica el contenido antes de reintentar.

* **ffmpeg no encontrado / no reproduce `.au`**
  → Asegúrate de que `ffmpeg` está en `PATH` y que `SongLibraryDir` es correcto.
  Prueba: `https://localhost:7165/api/audio/blues.00000`.

* **No salen recomendaciones (lista vacía)**
  → Falta `embeddings.npz` o se generó con otro modelo/preprocesado.
  **Solución:** genera el índice con `build_index.py` usando **tu** `modelo.h5` y la **misma** configuración de espectrogramas.

---

## 🔐 Seguridad (nota rápida)

El proyecto está en modo **Development** por defecto y expone endpoints simples.
Para producción: valida CORS, límites de tamaño, autenticación/autoría, limpiar temporales, etc.


## 👋 Contacto

Si te atoras con alguna ruta o error, revisa:

* `appsettings.json` (paths reales)
* Consola de la app (.NET) y consola Python
* `wwwroot` accesible y puertos que imprime `dotnet run`.
