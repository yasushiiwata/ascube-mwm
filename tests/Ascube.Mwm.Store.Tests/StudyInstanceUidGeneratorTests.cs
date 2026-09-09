using System.Text.RegularExpressions;

namespace Ascube.Mwm.Store.Tests;

public partial class StudyInstanceUidGeneratorTests
{
    [GeneratedRegex(@"^2\.25\.\d{1,39}$")]
    private static partial Regex UidPattern();

    [Fact]
    public void NewUid_MatchesExpectedFormat()
    {
        for (var i = 0; i < 1000; i++)
        {
            var uid = StudyInstanceUidGenerator.NewUid();
            Assert.Matches(UidPattern(), uid);
        }
    }

    [Fact]
    public void NewUid_OneMillionGenerations_HaveNoDuplicates()
    {
        const int count = 1_000_000;
        var seen = new HashSet<string>(count);

        for (var i = 0; i < count; i++)
        {
            Assert.True(seen.Add(StudyInstanceUidGenerator.NewUid()), "重複したUIDが生成された");
        }

        Assert.Equal(count, seen.Count);
    }
}
