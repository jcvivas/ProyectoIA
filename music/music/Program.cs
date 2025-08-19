using Microsoft.AspNetCore.StaticFiles;
using music.Components;
using music.Infraestructure;
using music.Models;
using music.Options;
using music.Services;
using System.Net;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationHandler>();
builder.Services.AddTransient<LoggingHandler>();
builder.Services.AddTransient<RetryHandler>();



builder.Services.AddHttpClient<IRecommendationApi, RecommendationApi>()
    .AddHttpMessageHandler<CorrelationHandler>()
    .AddHttpMessageHandler<LoggingHandler>()
    .AddHttpMessageHandler<RetryHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<InferenceOptions>(builder.Configuration.GetSection(InferenceOptions.SectionName));
builder.Services.AddSingleton<IInferenceProcess, InferenceProcess>();
builder.Services.AddHttpClient(); // <— para inyectar IHttpClientFactory


builder.Services.AddHttpClient<IRecommendationApi, RecommendationApi>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg.GetSection("Api")["BaseUrl"] ?? "";
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        // Si algún día apuntas a una API externa, ponla aquí (https://api.tu-dominio.com/)
        http.BaseAddress = new Uri(baseUrl);
    }

    http.Timeout = TimeSpan.FromSeconds(100);
});

builder.Services.AddSingleton<music.Infraestructure.TelemetryStore>();



builder.Services.AddHttpClient<music.Services.IFeedbackApi, music.Services.FeedbackApi>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg.GetSection("Api")["BaseUrl"] ?? "";
    if (!string.IsNullOrWhiteSpace(baseUrl))
        http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();// opcional; no molesta en Server
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Mapea Razor Components UNA vez
app.MapRazorComponents<music.Components.App>()
   .AddInteractiveServerRenderMode();

// --- API /api/recomendar (tu código tal cual, sin cambios de lógica) ---
var api = app.MapGroup("/api");

api.MapPost("/recomendar", async (HttpRequest http, IInferenceProcess infer, IConfiguration cfg,
                                  music.Infraestructure.TelemetryStore stats, CancellationToken ct) =>
{
    var opt = cfg.GetSection(InferenceOptions.SectionName).Get<InferenceOptions>()!;
    string? tempPath = null;
    string? audioPath = null;

    try
    {
        if (http.HasFormContentType)
        {
            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Falta 'file'." });

            var dir = string.IsNullOrWhiteSpace(opt.TempDir) ? Path.GetTempPath() : opt.TempDir;
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(file.FileName);
            tempPath = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
            await using var fs = File.Create(tempPath);
            if (file.Length > 100 * 1024 * 1024)
                return Results.BadRequest(new { error = "Archivo supera 100MB." });

            await using var s = file.OpenReadStream(); // sin parámetros
            await s.CopyToAsync(fs, ct);
            audioPath = tempPath;
        }
        else if (http.ContentType?.Contains("application/json") == true)
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("songId", out var sidElem))
                return Results.BadRequest(new { error = "Falta 'songId'." });

            var songId = sidElem.GetString();
            if (string.IsNullOrWhiteSpace(songId))
                return Results.BadRequest(new { error = "songId vacío." });

            audioPath = ResolveSongPath(opt.SongLibraryDir, songId!);
            if (audioPath is null) return Results.NotFound(new { error = "Audio no encontrado para songId." });
        }
        else
        {
            return Results.BadRequest(new { error = "Usa multipart/form-data o application/json." });
        }

        var result = await infer.RunAsync(audioPath!, ct);
        if (result?.recomendaciones is not null)
        {
            foreach (var r in result.recomendaciones)
            {
                if (!string.IsNullOrWhiteSpace(r.songId))
                    r.audioUrl = $"/api/audio/{Uri.EscapeDataString(r.songId)}";
            }
        }
        stats.AddPrediction(result.genero);

        return Results.Json(result);
    }
    catch (TimeoutException ex)
    {
        return Results.Problem(title: "Timeout en inferencia", detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Error en inferencia", detail: ex.Message, statusCode: 500);
    }
    finally
    {
        if (tempPath is not null) { try { File.Delete(tempPath); } catch { } }
    }

    static string? ResolveSongPath(string baseDir, string songId)
    {
        if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir)) return null;
        var exts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".au" };
        foreach (var ext in exts)
        {
            var p = Path.Combine(baseDir, songId + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }
});

api.MapGet("/health", (IConfiguration cfg) =>
{
    var opt = cfg.GetSection(InferenceOptions.SectionName).Get<InferenceOptions>()!;
    var report = new
    {
        PythonExe = new { opt.PythonExe, Exists = File.Exists(opt.PythonExe) },
        ScriptPath = new { opt.ScriptPath, Exists = File.Exists(opt.ScriptPath) },
        ModelPath = new { opt.ModelPath, Exists = File.Exists(opt.ModelPath) },
        LabelsPath = new { opt.LabelsPath, Exists = string.IsNullOrWhiteSpace(opt.LabelsPath) ? (bool?)null : File.Exists(opt.LabelsPath) },
        EmbeddingsIndexPath = new { opt.EmbeddingsIndexPath, Exists = string.IsNullOrWhiteSpace(opt.EmbeddingsIndexPath) ? (bool?)null : File.Exists(opt.EmbeddingsIndexPath) },
        TempDir = new { opt.TempDir, Exists = Directory.Exists(opt.TempDir) },
        SongLibraryDir = new { opt.SongLibraryDir, Exists = string.IsNullOrWhiteSpace(opt.SongLibraryDir) ? (bool?)null : Directory.Exists(opt.SongLibraryDir) }
    };
    return Results.Json(report);
});

api.MapGet("/audio/{songId}", (string songId, music.Infraestructure.TelemetryStore stats, IConfiguration cfg) =>
{
    stats.AddPlay(songId);
    var opt = cfg.GetSection(music.Options.InferenceOptions.SectionName).Get<music.Options.InferenceOptions>();
    if (opt is null || string.IsNullOrWhiteSpace(opt.SongLibraryDir))
        return Results.Problem("SongLibraryDir no configurado.", statusCode: 500);

    var path = ResolveSongPath(opt.SongLibraryDir, songId);
    if (path is null) return Results.NotFound();

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(path, out var contentType))
        contentType = "application/octet-stream";

    // Habilita rangos para poder buscar en el <audio>
    return Results.File(path, contentType, enableRangeProcessing: true);
});

// Reutiliza el mismo resolver que ya usas en /recomendar
static string? ResolveSongPath(string baseDir, string songId)
{
    if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir)) return null;
    var exts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".au" };
    foreach (var ext in exts)
    {
        var p = Path.Combine(baseDir, songId + ext);
        if (File.Exists(p)) return p;
    }
    return null;
}

api.MapPost("/feedback", async (HttpRequest req,
                                music.Infraestructure.TelemetryStore stats,
                                CancellationToken ct) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
    var root = doc.RootElement;

    bool? liked = root.TryGetProperty("liked", out var l) && l.ValueKind == JsonValueKind.True ? true :
                  root.TryGetProperty("liked", out l) && l.ValueKind == JsonValueKind.False ? false : (bool?)null;

    int? rating = root.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number
                    ? r.GetInt32() : (int?)null;

    IEnumerable<(string id, bool? liked)>? items = null;
    if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
    {
        var list = new List<(string, bool?)>();
        foreach (var el in arr.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            bool? iliked = el.TryGetProperty("liked", out var lk) && lk.ValueKind == JsonValueKind.True ? true :
                           el.TryGetProperty("liked", out lk) && lk.ValueKind == JsonValueKind.False ? false : (bool?)null;
            if (!string.IsNullOrWhiteSpace(id))
                list.Add((id!, iliked));
        }
        items = list;
    }

    stats.AddFeedback(liked, rating, items);
    return Results.Ok(new { ok = true });
});


api.MapGet("/stats", (music.Infraestructure.TelemetryStore stats) =>
{
    var snap = stats.Snapshot();
    return Results.Json(snap);
});


app.Run();
