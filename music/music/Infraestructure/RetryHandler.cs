namespace music.Infraestructure
{
    public class RetryHandler : DelegatingHandler
    {
        private static readonly HttpMethod[] Idempotent = new[] { HttpMethod.Get, HttpMethod.Head };



        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // 👇 No reintentes si no es idempotente o si hay body
            if (!Idempotent.Contains(request.Method) || request.Content is not null)
            {
                return await base.SendAsync(request, ct);
            }

            const int maxRetries = 2;
            var delayMs = 300;

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var resp = await base.SendAsync(request, ct);

                    // Reintenta solo para 5xx (si aún quedan intentos)
                    if ((int)resp.StatusCode >= 500 && attempt < maxRetries)
                    {
                        await Task.Delay(delayMs * (attempt + 1), ct);
                        continue;
                    }

                    return resp;
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    await Task.Delay(delayMs * (attempt + 1), ct);
                    continue;
                }
            }
        }
    }
}
