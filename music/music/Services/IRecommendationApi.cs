using Microsoft.AspNetCore.Components.Forms;
using music.Models;

namespace music.Services
{
    public interface IRecommendationApi
    {
        Task<RecommendationResponse> RecommendByFileAsync(IBrowserFile file, CancellationToken ct);
    }
}
