using System.Text.Json.Nodes;
using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

public class JsoncLoaderTests
{
    [Fact]
    public void Parse_AllowsCommentsAndTrailingCommas()
    {
        const string jsonc = """
            {
              // comment
              "a": 1,
              "b": { "c": 2, },
            }
            """;

        var obj = JsoncLoader.Parse(jsonc, "test");

        Assert.Equal(1, obj["a"]!.GetValue<int>());
        Assert.Equal(2, obj["b"]!["c"]!.GetValue<int>());
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsProfileFormatExceptionNamingTheSource()
    {
        const string broken = "{ invalid json ";

        var ex = Assert.Throws<ProfileFormatException>(() => JsoncLoader.Parse(broken, "test.jsonc"));

        Assert.Contains("test.jsonc", ex.Message);
    }

    [Fact]
    public void Parse_NonObjectRoot_Throws()
    {
        const string arrayRoot = "[1, 2, 3]";

        Assert.Throws<ProfileFormatException>(() => JsoncLoader.Parse(arrayRoot, "test.jsonc"));
    }

    [Fact]
    public void DeepMerge_ObjectsMergeRecursively_ButArraysAreReplacedWholesale()
    {
        var baseObj = (JsonObject)JsonNode.Parse("""{ "a": { "x": 1, "y": 2 }, "arr": [1, 2, 3] }""")!;
        var overlay = (JsonObject)JsonNode.Parse("""{ "a": { "y": 20, "z": 3 }, "arr": [9] }""")!;

        var merged = JsoncLoader.DeepMerge(baseObj, overlay);

        Assert.Equal(1, merged["a"]!["x"]!.GetValue<int>());
        Assert.Equal(20, merged["a"]!["y"]!.GetValue<int>());
        Assert.Equal(3, merged["a"]!["z"]!.GetValue<int>());
        Assert.Single(merged["arr"]!.AsArray());
        Assert.Equal(9, merged["arr"]![0]!.GetValue<int>());
    }

    [Fact]
    public void LoadMerged_BaseProfileMissing_ThrowsProfileFormatException()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Throws<ProfileFormatException>(() => JsoncLoader.LoadMerged("GENERIC", dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadMerged_ProfileMissing_ThrowsProfileFormatException()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "_base.jsonc"), "{ \"schemaVersion\": 1 }");

            Assert.Throws<ProfileFormatException>(() => JsoncLoader.LoadMerged("NO_SUCH_PROFILE", dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
