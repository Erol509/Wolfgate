using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.ScratchTickets;
using NUnit.Framework;

namespace Content.Tests._WF.ScratchTickets;

/// <summary>
/// The scratch ticket roll, in every format: the cells show the rolled winner's match and no other, and wins come as
/// often as the weights say.
/// </summary>
[TestFixture]
public sealed class WFScratchTicketRulesTest
{
    private const float LoseWeight = 600f;

    private static List<WFScratchSymbol> Symbols()
    {
        return new List<WFScratchSymbol>
        {
            new() { Prize = 1000, Weight = 100 },
            new() { Prize = 500, Weight = 300 },
            new() { Prize = 250, Weight = 0 },
            new(),
            new(),
            new(),
        };
    }

    /// <summary>Cells in a win: the match itself, plus the winning symbol it is matched against.</summary>
    private static int Matched(WFScratchFormat format)
    {
        return WFScratchTicketRules.Match(format) + (format == WFScratchFormat.WinningSymbol ? 1 : 0);
    }

    [Test]
    public void RolledCellsMatchTheOutcome([Values] WFScratchFormat format)
    {
        var symbols = Symbols();
        var random = new Random(4);
        var cells = new List<int>();
        var wins = new int[symbols.Count];
        const int rolls = 20000;

        for (var i = 0; i < rolls; i++)
        {
            var winner = WFScratchTicketRules.Roll(format, symbols, LoseWeight, random, cells);

            Assert.That(cells, Has.Count.EqualTo(WFScratchTicketRules.CellCount(format)));
            Assert.That(WFScratchTicketRules.Winner(format, cells), Is.EqualTo(winner), "The cells show a different outcome from the roll.");

            var winning = Enumerable.Range(0, cells.Count).Where(index => WFScratchTicketRules.Wins(format, cells, index)).ToList();
            Assert.That(winning, Has.Count.EqualTo(winner < 0 ? 0 : Matched(format)), "The wrong number of cells make up the win.");
            Assert.That(winning.Select(index => cells[index]), Is.All.EqualTo(winner));

            if (winner >= 0)
                wins[winner]++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(wins[0] / (float) rolls, Is.EqualTo(0.1f).Within(0.015f));
            Assert.That(wins[1] / (float) rolls, Is.EqualTo(0.3f).Within(0.015f));
            Assert.That(wins.Skip(2).Sum(), Is.Zero, "A symbol with no weight won.");
        });
    }
}
