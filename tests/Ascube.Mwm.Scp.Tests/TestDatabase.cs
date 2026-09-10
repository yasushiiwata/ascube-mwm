using Ascube.Mwm.Store;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// テストごとに使い捨てのSQLiteファイルパスを払い出す（Store.Tests の同名クラスと同じ役割。
/// T13：writer→実SQLite→repository の配線全体を通す W-01/W-02 テストのために Scp.Tests 側にも用意する）。
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly DirectoryInfo _dir;

    public TestDatabase()
    {
        _dir = Directory.CreateTempSubdirectory("ascube-mwm-scp-tests-db-");
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
