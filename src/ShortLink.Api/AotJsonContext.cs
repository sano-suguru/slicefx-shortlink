using System.Text.Json.Serialization;
using ShortLink.Api.Features.Health;
using ShortLink.Api.Features.Links;

namespace ShortLink.Api;

[SliceJsonContext(SliceJsonTarget.AspNet)]
[JsonSerializable(typeof(GetHealth.Response), TypeInfoPropertyName = "GetHealthResponse")]
[JsonSerializable(typeof(CreateLink.Request))]
[JsonSerializable(typeof(CreateLink.Response), TypeInfoPropertyName = "CreateLinkResponse")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotJsonContext : JsonSerializerContext { }
