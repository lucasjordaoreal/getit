using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GetIt_App.Models;

public class VideoMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = string.Empty;

    [JsonPropertyName("duration_string")]
    public string DurationString { get; set; } = string.Empty;

    [JsonPropertyName("uploader")]
    public string Uploader { get; set; } = string.Empty;

    [JsonPropertyName("formats")]
    public List<VideoFormat> Formats { get; set; } = new();
}

public class DownloadProgressInfo
{
    public double Percentage { get; set; }
    public string Speed { get; set; } = string.Empty;
    public string Eta { get; set; } = string.Empty;
}

public class VideoFormat
{
    [JsonPropertyName("format_id")]
    public string FormatId { get; set; } = string.Empty;

    [JsonPropertyName("ext")]
    public string Extension { get; set; } = string.Empty;

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("fps")]
    public double? Fps { get; set; }

    [JsonPropertyName("vcodec")]
    public string VideoCodec { get; set; } = string.Empty;

    [JsonPropertyName("acodec")]
    public string AudioCodec { get; set; } = string.Empty;

    [JsonPropertyName("format_note")]
    public string FormatNote { get; set; } = string.Empty;

    public bool HasVideo => VideoCodec != "none" && !string.IsNullOrEmpty(VideoCodec);
    public bool HasAudio => AudioCodec != "none" && !string.IsNullOrEmpty(AudioCodec);
    
    public string DisplayName 
    {
        get
        {
            string res = !string.IsNullOrWhiteSpace(Resolution) ? Resolution : "N/A";
            string note = !string.IsNullOrWhiteSpace(FormatNote) ? FormatNote : res;
            
            // If the note doesn't contain the resolution but resolution is valid, we can combine them,
            // or just use note if it's meaningful (like "1080p"). Often note is "1080p" and res is "1920x1080".
            // We will just use the resolution if available.
            string displayRes = !string.IsNullOrWhiteSpace(Resolution) && Resolution.Contains("x") ? Resolution : note;

            if (Fps.HasValue && Fps.Value > 0)
            {
                return $"{displayRes} ({Fps.Value} fps)";
            }
            return displayRes;
        }
    }
}

