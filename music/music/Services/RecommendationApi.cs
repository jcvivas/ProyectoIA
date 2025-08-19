using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using music.Models;

namespace music.Services;

public class RecommendationApi : IRecommendationApi
{
    private readonly HttpClient _http;

    public RecommendationApi(HttpClient http, NavigationManager nav)
    {
        _http = http;

        // Si no llegó BaseAddress desde appsettings, usa la base del sitio actual (https://localhost:7165/)
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(nav.BaseUri);
        }
    }

    public async Task<RecommendationResponse> RecommendByFileAsync(IBrowserFile file, CancellationToken ct)
    {
        byte[] bytes;
        using (var input = file.OpenReadStream(long.MaxValue, ct))
        using (var ms = new MemoryStream())
        {
            await input.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(fileContent, "file", file.Name); // la API espera "file"

        using var res = await _http.PostAsync("api/recomendar", content, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"API {(int)res.StatusCode}: {body}");

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<RecommendationResponse>(body, opts) ?? new RecommendationResponse();

    }
}
