namespace Ascube.Mwm.Store.Tests;

/// <summary>テストごとに使い捨てのSQLiteファイルパスを払い出す。</summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly DirectoryInfo _dir;

    public TestDatabase()
    {
        _dir = Directory.CreateTempSubdirectory("ascube-mwm-store-tests-");
        Path = System.IO.Path.Combine(_dir.FullName, "mwm.db");
    }

    public string Path { get; }

    public MwmStoreOptions Options(TimeSpan? currentTtl = null) => new()
    {
        DatabasePath = Path,
        DeviceProfileId = "TEST_PROFILE",
        CurrentTtl = currentTtl ?? TimeSpan.FromMinutes(15),
    };

    public void Dispose()
    {
        try
        {
            _dir.Delete(recursive: true);
        }
        catch (IOException)
        {
            // WAL/SHM ファイルがまだハンドルを持っている場合がある。テストの後始末なので無視してよい。
        }
    }
}
