using music.Models;

namespace music.Services
{
    public interface IInferenceProcess
    {
        Task<RecommendationResponse> RunAsync(string audioPath, CancellationToken ct);
    }
}
