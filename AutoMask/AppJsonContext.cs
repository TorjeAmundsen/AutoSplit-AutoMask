using System.Text.Json.Serialization;

namespace AutoSplit_AutoMask;

[JsonSerializable(typeof(SplitPreset))]
[JsonSerializable(typeof(PremadeSplitsFile))]
[JsonSerializable(typeof(PremadeSplit))]
[JsonSerializable(typeof(CapturePreferences))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext;
