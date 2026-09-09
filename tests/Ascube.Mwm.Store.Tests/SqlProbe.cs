using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store.Tests;

/// <summary>テストの検証専用。テスト対象コードを経由せず生SQLで実データを覗く。</summary>
internal static class SqlProbe
{
    public static async Task<int> CountAsync(string dbPath, string table)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task<string?> ScalarAsync(string dbPath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync()) as string;
    }
}
