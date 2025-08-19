using System.Collections.Concurrent;
using music.Models;

namespace music.Infraestructure
{
    public class TelemetryStore
    {
        private readonly ConcurrentDictionary<string, int> _porGenero =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _plays =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _likesPorCancion =
            new(StringComparer.OrdinalIgnoreCase);

        private int _totalPredicciones;
        private int _likesTotales;
        private int _dislikesTotales;

        private readonly object _ratingLock = new();
        private double _ratingSum;
        private int _ratingCount;

        public void AddPrediction(string? genero)
        {
            Interlocked.Increment(ref _totalPredicciones);
            if (!string.IsNullOrWhiteSpace(genero))
                _porGenero.AddOrUpdate(genero, 1, (_, v) => v + 1);
        }

        public void AddPlay(string? songId)
        {
            if (string.IsNullOrWhiteSpace(songId)) return;
            _plays.AddOrUpdate(songId, 1, (_, v) => v + 1);
        }

        public void AddFeedback(bool? liked, int? rating,
                        IEnumerable<(string id, bool? liked)>? items)
        {
            // rating promedio
            if (rating is int r && r > 0)
            {
                lock (_ratingLock) { _ratingSum += r; _ratingCount++; }
            }

            // Si el usuario marcó like/dislike por canción, contamos por item.
            // Si NO marcó por canción, caemos al like/dislike global.
            var anyItemVote = false;

            if (items is not null)
            {
                foreach (var it in items)
                {
                    if (string.IsNullOrWhiteSpace(it.id)) continue;
                    if (it.liked == true)
                    {
                        anyItemVote = true;
                        Interlocked.Increment(ref _likesTotales); // cuenta global
                        _likesPorCancion.AddOrUpdate(it.id, 1, (_, v) => v + 1); // por-canción
                    }
                    else if (it.liked == false)
                    {
                        anyItemVote = true;
                        Interlocked.Increment(ref _dislikesTotales); // cuenta global
                    }
                }
            }

            // Si no hubo votos por item, tomamos el like/dislike global (sí/no)
            if (!anyItemVote && liked.HasValue)
            {
                if (liked.Value) Interlocked.Increment(ref _likesTotales);
                else Interlocked.Increment(ref _dislikesTotales);
            }
        }


        public StatsResponse Snapshot()
        {
            double avg = 0; int count;
            lock (_ratingLock)
            {
                count = _ratingCount;
                if (count > 0) avg = _ratingSum / count;
            }

            return new StatsResponse
            {
                totalPredicciones = _totalPredicciones,
                porGenero = _porGenero.Select(kv => new GenreCount(kv.Key, kv.Value))
                                      .OrderByDescending(x => x.conteo)
                                      .ToList(),
                likesTotales = _likesTotales,
                dislikesTotales = _dislikesTotales,
                ratingPromedio = Math.Round(avg, 2),
                ratingsCount = count,
                topReproducciones = _plays.OrderByDescending(kv => kv.Value)
                                          .Take(10)
                                          .Select(kv => new ItemCount(kv.Key, kv.Value))
                                          .ToList(),
                topLikes = _likesPorCancion.OrderByDescending(kv => kv.Value)
                                           .Take(10)
                                           .Select(kv => new ItemCount(kv.Key, kv.Value))
                                           .ToList(),
            };
        }
    }
}
