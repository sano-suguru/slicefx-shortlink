using System.Text.Json.Serialization;
using ShortLink.Contracts;

namespace ShortLink.Api;

[SliceJsonContext(SliceJsonTarget.AspNet)]
[JsonSerializable(typeof(GetHealthResponse))]
[JsonSerializable(typeof(CreateLinkRequest))]
[JsonSerializable(typeof(CreateLinkResponse), TypeInfoPropertyName = "CreateLinkResponse")]
[JsonSerializable(typeof(ListLinksResponse), TypeInfoPropertyName = "ListLinksResponse")]
[JsonSerializable(typeof(LinkItem))]
[JsonSerializable(typeof(GetLinkStatsResponse), TypeInfoPropertyName = "GetLinkStatsResponse")]
[JsonSerializable(typeof(DailyClicksItem))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotJsonContext : JsonSerializerContext { }
