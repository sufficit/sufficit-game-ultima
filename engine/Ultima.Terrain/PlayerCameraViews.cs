namespace Ultima.Terrain;

internal enum PlayerCameraView
{
    Rear,
    FirstPerson,
    Close
}

internal static class PlayerCameraViews
{
    internal const double EyeHeight = 1.62;

    internal static PlayerCameraView Next(PlayerCameraView view) => view switch
    {
        PlayerCameraView.Rear => PlayerCameraView.FirstPerson,
        PlayerCameraView.FirstPerson => PlayerCameraView.Close,
        _ => PlayerCameraView.Rear,
    };
}
