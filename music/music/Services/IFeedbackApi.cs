using music.Models;

namespace music.Services
{
    public interface IFeedbackApi
    {
        Task SendAsync(RecommendationFeedback payload, CancellationToken ct = default);
    }
}
