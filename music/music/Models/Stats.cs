namespace music.Models
{
    public record GenreCount(string genero, int conteo);
    public record ItemCount(string id, int conteo);

    public class StatsResponse
    {
        public int totalPredicciones { get; set; }
        public List<GenreCount> porGenero { get; set; } = new();
        public int likesTotales { get; set; }
        public int dislikesTotales { get; set; }
        public double ratingPromedio { get; set; }
        public int ratingsCount { get; set; }
        public List<ItemCount> topReproducciones { get; set; } = new();
        public List<ItemCount> topLikes { get; set; } = new();
    }
}
