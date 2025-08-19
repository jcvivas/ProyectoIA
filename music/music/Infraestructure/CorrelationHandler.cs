namespace music.Infraestructure
{
    public class CorrelationHandler: DelegatingHandler
    {
        public const string HeaderName = "X-Correlation-ID";
        private readonly IHttpContextAccessor _http;

        public CorrelationHandler(IHttpContextAccessor http) => _http = http;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cid = _http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
            if (!request.Headers.Contains(HeaderName))
                request.Headers.TryAddWithoutValidation(HeaderName, cid);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
