namespace BlazorS3Publish.Models;

internal sealed class EndpointDefinition
{
    public string? Route { get; set; }

    public string? AssetFile { get; set; }

    public List<NameValueEntry>? EndpointProperties { get; set; }

    public List<NameValueEntry>? ResponseHeaders { get; set; }
}
