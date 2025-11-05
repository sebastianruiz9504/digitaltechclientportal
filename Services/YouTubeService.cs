using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalTechClientPortal.Services;

public class YouTubeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YouTubeService> _logger;
    private readonly YouTubeOptions _options;

    public YouTubeService(HttpClient httpClient, ILogger<YouTubeService> logger, IOptions<YouTubeOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<List<YouTubeVideoDto>> GetPlaylistVideosAsync()
    {
        var videos = new List<YouTubeVideoDto>();

        // Guard clauses de configuración
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.PlaylistId))
        {
            _logger.LogError("YouTubeOptions incompletas: ApiKey o PlaylistId ausentes.");
            return videos;
        }

        string? nextPageToken = null;

        try
        {
            do
            {
                var url =
                    $"https://www.googleapis.com/youtube/v3/playlistItems" +
                    $"?part=snippet&maxResults=50&playlistId={_options.PlaylistId}&key={_options.ApiKey}" +
                    (string.IsNullOrEmpty(nextPageToken) ? "" : $"&pageToken={nextPageToken}");

                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                nextPageToken = doc.RootElement.TryGetProperty("nextPageToken", out var tokenEl)
                    ? tokenEl.GetString()
                    : null;

                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("Respuesta sin 'items' o formato incorrecto.");
                    break;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("snippet", out var snippet) || snippet.ValueKind != JsonValueKind.Object)
                    {
                        _logger.LogWarning("Elemento sin snippet válido, se omite.");
                        continue;
                    }

                    var title = snippet.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                    var description = snippet.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";

                    string? videoId = null;
                    if (snippet.TryGetProperty("resourceId", out var resourceIdEl) &&
                        resourceIdEl.ValueKind == JsonValueKind.Object &&
                        resourceIdEl.TryGetProperty("videoId", out var vidEl))
                    {
                        videoId = vidEl.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(videoId))
                    {
                        _logger.LogWarning("Video omitido (sin videoId) — título: {Title}", title);
                        continue;
                    }

                    string thumbnailUrl = "";
                    if (snippet.TryGetProperty("thumbnails", out var thumbsEl) &&
                        thumbsEl.ValueKind == JsonValueKind.Object &&
                        thumbsEl.TryGetProperty("medium", out var mediumEl) &&
                        mediumEl.ValueKind == JsonValueKind.Object &&
                        mediumEl.TryGetProperty("url", out var urlEl))
                    {
                        thumbnailUrl = urlEl.GetString() ?? "";
                    }

                    videos.Add(new YouTubeVideoDto
                    {
                        VideoId = videoId,
                        Title = title,
                        ThumbnailUrl = thumbnailUrl,
                        Description = description
                    });
                }
            }
            while (!string.IsNullOrEmpty(nextPageToken));
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "Error HTTP al obtener videos de YouTube.");
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error de parseo JSON al obtener videos de YouTube.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al obtener videos de YouTube.");
        }

        return videos;
    }
}

public class YouTubeVideoDto
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}