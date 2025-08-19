namespace music.Models
{
    public class RecommendationFeedback
    {
        public string sessionId { get; set; } = Guid.NewGuid().ToString("N");
        public string genero { get; set; } = "";
        public bool? liked { get; set; }        // like/dislike general
        public int? rating { get; set; }        // 1..5
        public string? comment { get; set; }    // opcional
        public List<FeedbackItem> items { get; set; } = new();
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    public class FeedbackItem
    {
        public string id { get; set; } = "";       // ej: "rock.00011"
        public string titulo { get; set; } = "";
        public bool? liked { get; set; }           // like/dislike por pista
    }
}
