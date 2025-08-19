namespace music.Models
{
    public class RecommendationItem
    {
        public string titulo { get; set; } = "";
        public string artista { get; set; } = "";
        public string caratula { get; set; } = "";

        public string? songId { get; set; }

        public string? audioUrl { get; set; }
    }
}
