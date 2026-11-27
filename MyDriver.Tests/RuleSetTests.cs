using MyDriver.Core.Diagnosis;

namespace MyDriver.Tests;

public class RuleSetTests
{
    [Fact]
    public void EmbeddedRules_AreValid()
    {
        var rules = RuleSet.LoadEmbedded();

        Assert.True(rules.Version >= 1);
        Assert.Empty(rules.Validate());
        Assert.Single(rules.Rules, r => r.Match.Otherwise);
    }

    [Fact]
    public void EmbeddedRules_CoverEveryErrorCodeInPrd()
    {
        var rules = RuleSet.LoadEmbedded().Rules.Where(r => r.Match.Type == MatchTypes.DeviceError).ToList();
        int[] prdCodes = [1, 3, 10, 12, 14, 18, 19, 22, 24, 28, 31, 32, 37, 39, 43, 45, 48, 52];

        Assert.All(prdCodes, code => Assert.Single(rules, r => r.Match.MatchesCode(code)));
    }

    [Fact]
    public void EmbeddedRules_UseOnlyKnownActions_AndRedYellowRulesHaveAtLeastOne()
    {
        foreach (var rule in RuleSet.LoadEmbedded().Rules)
        {
            Assert.All(rule.Actions, a => Assert.True(ActionCatalog.IsKnown(a), $"{rule.Id}: {a}"));
            if (rule.Level != FindingLevel.Info)
            {
                Assert.NotEmpty(rule.Actions);
            }
        }
    }

    [Fact]
    public void Validate_ReportsUnknownActionsTypesAndDuplicates()
    {
        var bad = new RuleSet
        {
            Version = 1,
            Rules =
            [
                new DiagnosisRule { Id = "a", Match = new RuleMatch { Type = "deviceError", Code = 1 }, Level = FindingLevel.Critical, Title = "t", Explanation = "e", Actions = ["terbang"] },
                new DiagnosisRule { Id = "a", Match = new RuleMatch { Type = "cuaca" }, Level = FindingLevel.Info, Title = "t", Explanation = "e" },
                new DiagnosisRule { Id = "b", Match = new RuleMatch { Type = "deviceError" }, Level = FindingLevel.Info, Title = "t", Explanation = "e" },
            ],
        };

        var errors = bad.Validate();

        Assert.Contains(errors, e => e.Contains("ganda"));
        Assert.Contains(errors, e => e.Contains("terbang"));
        Assert.Contains(errors, e => e.Contains("cuaca"));
        Assert.Contains(errors, e => e.StartsWith("b:"));
    }

    [Fact]
    public void Load_MissingFile_WritesEmbeddedCopy()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "rules.json");

        var rules = RuleSet.Load(file, new MemoryLog());

        Assert.True(File.Exists(file));
        Assert.Equal(RuleSet.LoadEmbedded().Rules.Count, rules.Rules.Count);
    }

    [Fact]
    public void Load_NewerValidFile_OverridesEmbedded()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "rules.json");
        File.WriteAllText(file, """
            { "version": 99, "rules": [ { "id": "cm-43", "match": { "type": "deviceError", "code": 43 },
              "level": "warning", "title": "{device} diganti", "explanation": "x", "actions": ["restartDevice"] } ] }
            """);

        var rules = RuleSet.Load(file, new MemoryLog());

        Assert.Equal(99, rules.Version);
        Assert.Equal(FindingLevel.Warning, Assert.Single(rules.Rules).Level);
    }

    [Fact]
    public void Load_OlderFile_IsReplacedByEmbedded()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "rules.json");
        File.WriteAllText(file, """{ "version": 0, "rules": [] }""");

        var rules = RuleSet.Load(file, new MemoryLog());

        Assert.Equal(RuleSet.LoadEmbedded().Version, rules.Version);
        Assert.Equal(RuleSet.LoadEmbedded().Version, RuleSet.Load(file, new MemoryLog()).Version);
        Assert.Contains("cm-43", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("{ rusak")]
    [InlineData("""{ "version": 99, "rules": [ { "id": "x", "match": { "type": "deviceError", "code": 1 }, "level": "critical", "title": "t", "explanation": "e", "actions": ["tidakAda"] } ] }""")]
    public void Load_CorruptOrInvalidFile_FallsBackToEmbedded(string content)
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "rules.json");
        File.WriteAllText(file, content);
        var log = new MemoryLog();

        var rules = RuleSet.Load(file, log);

        Assert.Equal(RuleSet.LoadEmbedded().Rules.Count, rules.Rules.Count);
        Assert.Contains(log.Entries, e => e.Level == MyDriver.Core.Logging.LogLevel.Warning);
    }
}
