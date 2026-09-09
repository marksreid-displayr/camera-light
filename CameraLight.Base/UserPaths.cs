namespace CameraLight.Base;

/// <summary>
/// Where per-user state lives. Deliberately not beside the executable: the app is deployed by
/// copying over C:\Apps, which would take the settings and history with it.
/// </summary>
public static class UserPaths
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CameraLight");

    public static string Settings => Path.Combine(Directory, "settings.json");

    public static string History => Path.Combine(Directory, "history.jsonl");

    public static void EnsureDirectory() => System.IO.Directory.CreateDirectory(Directory);
}
