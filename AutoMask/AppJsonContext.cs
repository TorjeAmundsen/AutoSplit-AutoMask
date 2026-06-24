using System.Text.Json.Serialization;

namespace AutoSplit_AutoMask;

// Nested generic and element types are registered explicitly so trimming/AOT can't remove
// reflection metadata the source-gen serializer relies on. Keep this in sync with any new
// model field types.
[JsonSerializable(typeof(SplitPreset))]
[JsonSerializable(typeof(Split))]
[JsonSerializable(typeof(List<Split>))]
[JsonSerializable(typeof(PremadeSplitsFile))]
[JsonSerializable(typeof(PremadeSplit))]
[JsonSerializable(typeof(List<PremadeSplit>))]
[JsonSerializable(typeof(CapturePreferences))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext;
