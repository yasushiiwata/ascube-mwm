using Ascube.Mwm.Store.Audit;

namespace Ascube.Mwm.Store.Tests;

/// <summary>テストごとに使い捨ての監査DBファイルパスを払い出す。</summary>
internal sealed class TestAuditDatabase : IDisposable
{
    private readonly DirectoryInfo _dir;

    public TestAuditDatabase()
    {
        _dir = Directory.CreateTempSubdirectory("ascube-mwm-audit-tests-");
        Path = System.IO.Path.Combine(_dir.FullName, "mwm-audit.db");
    }

    public string Path { get; }

    public AuditStoreOptions Options() => new() { DatabasePath = Path };

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
