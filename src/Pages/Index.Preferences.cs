using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Ultima.Pages;

public partial class Index
{
    private const string PreferencesKey = "ultima.preferencias.v1";
    private string _preferredBuildAsset = string.Empty;

    private async Task LoadLocalPreferencesAsync()
    {
        try
        {
            var cpuModel = await JS.InvokeAsync<string?>("game.browser.read", "ultima-ia-cpu-modelo");
            if (cpuModel is not null && AiCpuModels.ContainsKey(cpuModel))
                _aiCpuModel = cpuModel;
            var json = await JS.InvokeAsync<string?>("game.browser.read", PreferencesKey);
            if (json is not null && JsonSerializer.Deserialize(json, LocalPreferencesJson.Default.LocalPreferences) is { } saved)
            {
                _devHud = saved.DevHud;
                _windowContrast = saved.WindowContrast;
                _workshopOpen = saved.WorkshopOpen;
                _brushRadius = saved.BrushRadius is >= 2 and <= 8 ? saved.BrushRadius : _brushRadius;
                _uploadScale = saved.UploadScale is >= 0.05 and <= 12 ? saved.UploadScale : _uploadScale;
                if (saved.PaintMaterial is "sand" or "grass" or "dirt" or "rock" or "snow" or "water")
                    _paintMaterial = saved.PaintMaterial;
                if (saved.BuildAsset is { Length: <= 160 } asset)
                    _preferredBuildAsset = asset;
            }
        }
        catch (Exception ex) when (ex is JSException or JsonException) { /* preferências locais opcionais */ }
    }

    private Task SaveLocalPreferencesAsync()
    {
        var saved = new LocalPreferences(_devHud, _windowContrast, _workshopOpen,
            _brushRadius, _uploadScale, _paintMaterial, _preferredBuildAsset);
        return JS.InvokeVoidAsync("game.browser.write", PreferencesKey,
            JsonSerializer.Serialize(saved, LocalPreferencesJson.Default.LocalPreferences)).AsTask();
    }
}

internal sealed record LocalPreferences(bool DevHud, bool WindowContrast, bool WorkshopOpen,
    double BrushRadius, double UploadScale, string? PaintMaterial, string? BuildAsset);

[JsonSerializable(typeof(LocalPreferences))]
internal sealed partial class LocalPreferencesJson : JsonSerializerContext;
