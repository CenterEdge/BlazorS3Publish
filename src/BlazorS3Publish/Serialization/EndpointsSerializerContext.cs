using System.Text.Json.Serialization;
using BlazorS3Publish.Models;

namespace BlazorS3Publish.Serialization;

[JsonSourceGenerationOptions(
    // The same physical asset can appear more than once: once from manifest routing and again
    // from the fallback file scan. Track final keys so we never overwrite metadata from a
    // duplicate second upload and never pay duplicate transfer cost.
    PropertyNameCaseInsensitive = true,
    // We only deserialize, so we don't need to generate fast-path serialization code.
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(EndpointsManifest))]
internal sealed partial class EndpointsSerializerContext : JsonSerializerContext
{
}
