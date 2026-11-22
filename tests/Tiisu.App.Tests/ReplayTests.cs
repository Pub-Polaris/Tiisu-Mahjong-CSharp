using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class ReplayTests
{
    private static ReplayRound DemoRound()
    {
        var headers = new[]
        {
            new ReplayHeader(0, "你", "东", true, true, "你"),
            new ReplayHeader(1, "AI 南", "南", false, false, null),
            new ReplayHeader(2, "AI 西", "西", false, false, null),
            new ReplayHeader(3, "AI 北", "北", false, false, null),
        };

        IReadOnlyList<ReplayMeld> none = Array.Empty<ReplayMeld>();
        var noMelds = new[] { none, none, none, none };
        var rivers = new[] { "", "", "", "" };
        var allHands = new[]
        {
            "123m456p789s1122z5z",
            "234m567p678s3344z",
            "345m678p789s5566z",
            "456m789p123s7788z",
        };
        var scores = new[] { 25000, 25000, 25000, 25000 };
        var handCounts = new[] { 14, 13, 13, 13 };
        var steps = new List<ReplayStep>
        {
            new(0, "draw", null, "123m456p789s1122z5z", 69, null, null, rivers,
                allHands, noMelds, handCounts, scores, 0, none),
            new(0, "discard", "5z", "123m456p789s1122z", 69, null, null, rivers,
                allHands, noMelds, new[] { 13, 13, 13, 13 }, scores, 0, none),
            new(1, "draw", null, "234m567p678s3344z5m", 68, null, null, rivers,
                allHands, noMelds, new[] { 13, 14, 13, 13 }, scores, 1, none),
        };

        return new ReplayRound("demo.jsonl", 1, 0, 0, headers, steps);
    }

    [Fact]
    public void Discard_is_graded_only_when_fourteen_tile_shape_is_known()
    {
        ReplayReport report = ReplayAnalysis.Analyze(DemoRound());

        Assert.Single(report.Grades);
        DiscardGrade grade = report.Grades[1];
        Assert.Equal(0, grade.Player);
        Assert.Equal("5z", grade.Tile);
        Assert.True(grade.Best);
        Assert.Equal(0, grade.Loss);
        Assert.Equal(1, report.Seats[0].Evaluated);
        Assert.Equal(1, report.Seats[0].Best);
        Assert.Equal(0, report.Seats[1].Evaluated);
    }

    [Fact]
    public void Parse_hand_supports_suits_honors_and_spaces()
    {
        List<Tiisu.Core.Tile> hand = ReplayAnalysis.ParseHand("123m 456p789s1122z");

        Assert.Equal(3 + 3 + 3 + 4, hand.Count);
        Assert.Equal(Tiisu.Core.Suit.Man, hand[0].Suit);
        Assert.Equal(Tiisu.Core.Suit.Honor, hand[^1].Suit);
    }

    [Fact]
    public void Replay_table_rotates_viewer_and_keeps_absolute_seat()
    {
        ReplayRound round = DemoRound();
        TableViewModel vm = ReplayTableModel.Build(round, round.Steps[1], viewerSeat: 2);

        Assert.Equal(4, vm.Seats.Count);
        Assert.Equal(2, vm.Seats[0].AbsoluteIndex);
        Assert.Equal("AI 西", vm.Seats[0].Name);
        Assert.Equal(2, vm.TurnIndex); // actor seat 0, viewer 2 -> display 2
        Assert.Equal(13, vm.Seats[0].HandCount);
        Assert.True(vm.RevealHands);
        Assert.Equal(0, vm.SelfSeat);
    }

    [Fact]
    public void Reader_parses_new_fields_and_old_logs_still_work()
    {
        string[] lines =
        {
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":0,\"name\":\"human\",\"nameText\":\"你\",\"human\":true,\"wind\":0,\"dealer\":true,\"roundWindIdx\":0}",
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":1,\"name\":\"ai_south\",\"nameText\":\"AI 南\",\"human\":false,\"wind\":1,\"dealer\":false,\"roundWindIdx\":0}",
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":2,\"name\":\"ai_west\",\"nameText\":\"AI 西\",\"human\":false,\"wind\":2,\"dealer\":false,\"roundWindIdx\":0}",
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":3,\"name\":\"ai_north\",\"nameText\":\"AI 北\",\"human\":false,\"wind\":3,\"dealer\":false,\"roundWindIdx\":0}",
            "{\"type\":\"step\",\"p\":0,\"a\":\"discard\",\"t\":\"5z\",\"hand\":\"123m456p789s1122z\",\"wall\":69," +
            "\"rivers\":[\"\",\"\",\"\",\"\"],\"allHands\":[\"123m\",\"234m\",\"345m\",\"456m\"]," +
            "\"handCounts\":[13,13,13,13],\"scores\":[25000,24000,23000,22000],\"turn\":0," +
            "\"allMelds\":[[],[],[{\"k\":\"pon\",\"t\":\"123m\",\"tiles\":[\"1m\",\"2m\",\"3m\"],\"claimed\":1}],[]]," +
            "\"melds\":[{\"k\":\"chi\",\"t\":\"456p\",\"tiles\":[\"4p\",\"5p\",\"6p\"]}],\"meldCounts\":[0,0,1,0]}",
        };

        ReplayRound round = Assert.Single(RoundLogReader.Parse(lines, "test.jsonl"));
        ReplayStep step = Assert.Single(round.Steps);

        Assert.True(round.Headers[0].Human);
        Assert.Equal("你", round.Headers[0].DisplayName);
        Assert.Equal(4, step.AllHands!.Count);
        Assert.Equal(24000, step.Scores![1]);
        Assert.Equal(0, step.Turn);
        Assert.Equal(1, step.MeldCounts![2]);
        Assert.Equal("1m", step.AllMelds![2][0].Tiles[0]);
        Assert.Equal("6p", step.Melds![0].Tiles[2]);
    }

    [Fact]
    public void Reader_expands_old_compact_meld_string()
    {
        string[] lines =
        {
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":0,\"name\":\"human\",\"wind\":0,\"dealer\":true}",
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":1,\"name\":\"ai_south\",\"wind\":1,\"dealer\":false}",
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":2,\"name\":\"ai_west\",\"wind\":2,\"dealer\":false}",
            "{\"type\":\"round\",\"round\":1,\"honba\":0,\"player\":3,\"name\":\"ai_north\",\"wind\":3,\"dealer\":false}",
            "{\"type\":\"step\",\"p\":0,\"a\":\"discard\",\"t\":\"5z\",\"hand\":\"123m\",\"wall\":69,\"rivers\":[\"\",\"\",\"\",\"\"],\"melds\":[{\"k\":\"pon\",\"t\":\"123m\"}]}",
        };

        ReplayRound round = Assert.Single(RoundLogReader.Parse(lines));
        ReplayStep step = Assert.Single(round.Steps);

        Assert.Null(step.AllHands);
        Assert.NotNull(step.Melds);
        Assert.Equal(new[] { "1m", "2m", "3m" }, step.Melds![0].Tiles);
    }
}
