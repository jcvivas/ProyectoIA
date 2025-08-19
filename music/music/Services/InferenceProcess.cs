using Microsoft.Extensions.Options;
using music.Models;
using music.Options;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace music.Services
{
    public class InferenceProcess : IInferenceProcess
    {
        private readonly InferenceOptions _opt;
        private readonly ILogger<InferenceProcess> _logger;

        public InferenceProcess(IOptions<InferenceOptions> opt, ILogger<InferenceProcess> logger)
        {
            _opt = opt.Value;
            _logger = logger;
        }

        public async Task<RecommendationResponse> RunAsync(string audioPath, CancellationToken ct)
        {
            ValidateOptions();

            var psi = new ProcessStartInfo
            {
                FileName = _opt.PythonExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-X");
            psi.ArgumentList.Add("utf8");
            psi.ArgumentList.Add(_opt.ScriptPath);
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_opt.ModelPath);
            psi.ArgumentList.Add("--audio");
            psi.ArgumentList.Add(audioPath);

            if (!string.IsNullOrWhiteSpace(_opt.LabelsPath))
            {
                psi.ArgumentList.Add("--labels");
                psi.ArgumentList.Add(_opt.LabelsPath);
            }

            if (!string.IsNullOrWhiteSpace(_opt.EmbeddingsIndexPath) && File.Exists(_opt.EmbeddingsIndexPath))
            {
                psi.ArgumentList.Add("--index");
                psi.ArgumentList.Add(_opt.EmbeddingsIndexPath);

                // opcional:
                psi.ArgumentList.Add("--topk");
                psi.ArgumentList.Add("8");
            }

            psi.Environment["PYTHONIOENCODING"] = "utf-8";

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!proc.Start()) throw new InvalidOperationException("No se pudo iniciar el proceso de inferencia.");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(_opt.TimeoutSeconds, 10)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try { await proc.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                throw new TimeoutException("Tiempo de inferencia excedido.");
            }

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"Inferencia falló (exit {proc.ExitCode}). {stderr}");

            var json = stdout.ToString().Trim();
            var resp = JsonSerializer.Deserialize<RecommendationResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (resp is null) throw new InvalidOperationException("Salida JSON inválida de inferencia.");
            return resp;
        }

        private void ValidateOptions()
        {
            static void MustExist(string path, string name)
            {
                if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException($"{name} vacío.");
                if (!File.Exists(path)) throw new FileNotFoundException($"{name} no existe", path);
            }
            MustExist(_opt.PythonExe, nameof(_opt.PythonExe));
            MustExist(_opt.ScriptPath, nameof(_opt.ScriptPath));
            MustExist(_opt.ModelPath, nameof(_opt.ModelPath));
            if (!string.IsNullOrWhiteSpace(_opt.LabelsPath) && !File.Exists(_opt.LabelsPath))
                throw new FileNotFoundException($"{nameof(_opt.LabelsPath)} no existe", _opt.LabelsPath);

            if (!string.IsNullOrWhiteSpace(_opt.TempDir) && !Directory.Exists(_opt.TempDir))
                Directory.CreateDirectory(_opt.TempDir);
        }

        private static void TryKill(Process p)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        }
    }
}
