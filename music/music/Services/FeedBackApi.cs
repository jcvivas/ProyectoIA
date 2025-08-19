using System.Net.Http.Json;
using music.Models;

namespace music.Services
{
    public class FeedbackApi : IFeedbackApi
    {
        private readonly HttpClient _http;
        public FeedbackApi(HttpClient http) => _http = http;

        public async Task SendAsync(RecommendationFeedback payload, CancellationToken ct = default)
        {
            using var resp = await _http.PostAsJsonAsync("/api/feedback", payload, ct);
            resp.EnsureSuccessStatusCode();
        }
    }
}
