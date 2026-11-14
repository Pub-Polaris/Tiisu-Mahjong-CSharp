using System.Linq;
using Tiisu.App.Runtime;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class RoundLogReaderTests
{
    [Fact]
    public void Parse_ReadsHeadersStepsAndLastRound()
    {
        string[] lines =
        {
            """{"type":"round","round":1,"honba":0,"player":0,"name":"human","wind":0,"dealer":true,"roundWindIdx":0}""",
            """{"type":"round","round":1,"honba":0,"player":1,"name":"ai_south","wind":1,"dealer":false,"roundWindIdx":0}""",
            """{"type":"round","round":1,"honba":0,"player":2,"name":"ai_west","wind":2,"dealer":false,"roundWindIdx":0}""",
            """{"type":"round","round":1,"honba":0,"player":3,"name":"ai_north","wind":3,"dealer":false,"roundWindIdx":0}""",
            """{"type":"step","a":"discard","p":0,"t":"7m","hand":"123m","rivers":["7m","","",""],"wall":40}""",
            """{"type":"round","round":2,"honba":1,"player":0,"name":"human","wind":0,"dealer":true,"roundWindIdx":0}""",
            """{"type":"round","round":2,"honba":1,"player":1,"name":"ai_south","wind":1,"dealer":false,"roundWindIdx":0}""",
            """{"type":"round","round":2,"honba":1,"player":2,"name":"ai_west","wind":2,"dealer":false,"roundWindIdx":0}""",
            """{"type":"round","round":2,"honba":1,"player":3,"name":"ai_north","wind":3,"dealer":false,"roundWindIdx":0}""",
            """{"type":"step","a":"call","p":1,"hand":"456p","rivers":["","","",""],"wall":30}""",
        };

        var rounds = RoundLogReader.Parse(lines, "roundlog_test.jsonl");

        Assert.Equal(2, rounds.Count);
        Assert.Equal(2, rounds[^1].Round);
        Assert.Equal(1, rounds[^1].Honba);
        Assert.Equal(4, rounds[^1].Headers.Count);
        Assert.Single(rounds[^1].Steps);
        Assert.Equal("call", rounds[^1].Steps[0].Action);
        Assert.Equal("AI 南", RoundLogReader.PlayerName(rounds[^1].Headers, 1));
    }

    [Fact]
    public void ActionName_MapsKnownAndUnknown()
    {
        Assert.Equal("打出", RoundLogReader.ActionName("discard"));
        Assert.Equal("报听", RoundLogReader.ActionName("call"));
        Assert.Equal("something", RoundLogReader.ActionName("something"));
    }
}
