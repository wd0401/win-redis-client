namespace WinRedisClient.Infrastructure;

/// <summary>本地数据目录：%AppData%\WinRedis。</summary>
public static class StoragePaths
{
    public static string RootDirectory
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinRedis");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string ConnectionsFile => Path.Combine(RootDirectory, "connections.json");
    public static string SettingsFile => Path.Combine(RootDirectory, "settings.json");
    public static string HistoryFile => Path.Combine(RootDirectory, "history.json");

    public static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, content, System.Text.Encoding.UTF8);
        File.Move(temp, path, overwrite: true);
    }
}
