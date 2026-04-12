using System.Text.Json.Serialization;

namespace AutoSplit_AutoMask;

[JsonSerializable(typeof(SplitPreset))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext;
