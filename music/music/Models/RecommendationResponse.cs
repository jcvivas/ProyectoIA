namespace music.Models
{
    public class RecommendationResponse
    {
        public string genero { get; set; } = "";
        public List<RecommendationItem> recomendaciones { get; set; } = new();
    }
}
