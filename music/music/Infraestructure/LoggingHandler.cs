using System.Diagnostics;

namespace music.Infraestructure
{
    public class LoggingHandler : DelegatingHandler
    {
        private readonly ILogger<LoggingHandler> _logger;
        public LoggingHandler(ILogger<LoggingHandler> logger) => _logger = logger;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("HTTP {Method} {Url}", request.Method, request.RequestUri);
            try
            {
                var resp = await base.SendAsync(request, ct);
                sw.Stop();
                _logger.LogInformation("HTTP {Method} {Url} -> {Status} ({Elapsed} ms)",
                    request.Method, request.RequestUri, (int)resp.StatusCode, sw.ElapsedMilliseconds);
                return resp;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                sw.Stop();
                _logger.LogWarning(ex, "HTTP {Method} {Url} failed after {Elapsed} ms",
                    request.Method, request.RequestUri, sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
