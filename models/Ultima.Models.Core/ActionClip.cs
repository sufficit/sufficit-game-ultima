namespace Ultima.Models.Core;

/// <summary>Marcas normalizadas da ação, no mesmo relógio da pose.</summary>
public sealed record ActionMarker(string Name, double Time, string Payload);
public sealed record ActionClip(double Duration, double Bend, double Reach, ActionMarker[] Markers);
