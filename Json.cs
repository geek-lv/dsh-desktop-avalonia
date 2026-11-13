using System.Text.Json.Serialization;

namespace DshDesktop;

/// <summary>Saved window geometry (window.json).</summary>
internal sealed record SavedBounds(int X, int Y, double Width, double Height, bool Maximized);

/// <summary>Source-generated serializers: the app is published with Native AOT and full trimming,
/// where reflection-based System.Text.Json is switched off.</summary>
[JsonSerializable(typeof(SavedBounds))]
[JsonSerializable(typeof(string))]
internal sealed partial class AppJson : JsonSerializerContext;
