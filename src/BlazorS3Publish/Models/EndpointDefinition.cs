namespace BlazorS3Publish.Models;

internal sealed class EndpointDefinition
{
    public string? Route { get; init; }

    public string? AssetFile { get; init; }

    public List<NameValueEntry>? EndpointProperties { get; init; }

    public List<NameValueEntry>? ResponseHeaders { get; init; }
}
