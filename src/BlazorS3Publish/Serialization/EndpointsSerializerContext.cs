using System.Text.Json.Serialization;
using BlazorS3Publish.Models;

namespace BlazorS3Publish.Serialization;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(EndpointsManifest))]
internal sealed partial class EndpointsSerializerContext : JsonSerializerContext
{
}
