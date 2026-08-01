using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyricFever.Core.Providers.Spotify;

public sealed class AccessTokenJson
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("accessTokenExpirationTimestampMs")]
    public double AccessTokenExpirationTimestampMs { get; set; }

    [JsonPropertyName("isAnonymous")]
    public bool IsAnonymous { get; set; }
}

public sealed class SpotifyServerTime
{
    [JsonPropertyName("serverTime")]
    public int ServerTime { get; set; }
}

public sealed class SecretVersion
{
    [JsonPropertyName("latestSecretVersion")]
    public int LatestSecretVersion { get; set; }

    [JsonPropertyName("message")]
    public List<int> Message { get; set; } = new();
}

public sealed class SpotifyParent
{
    [JsonPropertyName("lyrics")]
    public SpotifyLyricsDto Lyrics { get; set; } = new();

    [JsonPropertyName("colors")]
    public SpotifyColorData Colors { get; set; } = new();
}

public sealed class SpotifyLyricsDto
{
    [JsonPropertyName("lines")]
    public List<SpotifyLyricLineDto>? Lines { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("syncType")]
    public string? SyncType { get; set; }
}

public sealed class SpotifyLyricLineDto
{
    [JsonPropertyName("startTimeMs")]
    public string StartTimeMs { get; set; } = "";

    [JsonPropertyName("words")]
    public string Words { get; set; } = "";
}

public sealed class SpotifyColorData
{
    [JsonPropertyName("background")]
    public int Background { get; set; }

    [JsonPropertyName("text")]
    public int Text { get; set; }

    [JsonPropertyName("highlightText")]
    public int HighlightText { get; set; }
}

public sealed class ErrorWrapper
{
    [JsonPropertyName("error")]
    public ErrorInfo Error { get; set; } = new();

    public sealed class ErrorInfo
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }
}

public sealed class SpotifyLyricError : Exception
{
    public enum ErrorKind
    {
        IsLocalFile,
        TooManyTries,
        Unauthorized,
        InvalidResponse,
        HttpStatus
    }

    public ErrorKind Kind { get; }

    public SpotifyLyricError(ErrorKind kind, int statusCode = 0)
        : base(kind switch
        {
            ErrorKind.IsLocalFile => "Spotify local files do not provide network lyrics.",
            ErrorKind.TooManyTries => "Spotify rate limited the lyric request.",
            ErrorKind.Unauthorized => "Spotify authorization expired.",
            ErrorKind.InvalidResponse => "Spotify returned an invalid response.",
            ErrorKind.HttpStatus => $"Spotify request failed with HTTP status {statusCode}.",
            _ => "Spotify error."
        })
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
