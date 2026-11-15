using System.Globalization;
using Shouldly;
using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;
using SplitBill.Domain.Splitting;

namespace SplitBill.Domain.Tests.Splitting;

/// <summary>
/// Team rule for EQUAL splits: round total / n to two decimals (third decimal below 5 rounds
/// down, 5 or above rounds up) and let the participant in position 1 absorb the cent
/// difference so that the shares always add up to the total.
/// </summary>
public sealed class SplitCalculatorTests
{
    private static readonly Currency Mxn = Currency.Mxn;

    private static Money Mxn_(string amount) =>
        Money.Of(decimal.Parse(amount, CultureInfo.InvariantCulture), Mxn);

    // ---------------------------------------------------------------- EQUAL

    [Fact]
    public void SplitEqually_HundredAmongThree_FirstParticipantAbsorbsPlusOneCent()
    {
        // 100 / 3 = 33.333… → 33.33 each = 99.99 → +0.01 goes to position 1
        var shares = SplitCalculator.SplitEqually(Mxn_("100"), participantCount: 3);

        shares.ShouldBe([Mxn_("33.34"), Mxn_("33.33"), Mxn_("33.33")]);
    }

    [Fact]
    public void SplitEqually_HundredAmongSeven_FirstParticipantAbsorbsMinusThreeCents()
    {
        // 100 / 7 = 14.2857… → 14.29 each = 100.03 → −0.03 goes to position 1
        var shares = SplitCalculator.SplitEqually(Mxn_("100"), participantCount: 7);

        shares.ShouldBe([
            Mxn_("14.26"), Mxn_("14.29"), Mxn_("14.29"), Mxn_("14.29"),
            Mxn_("14.29"), Mxn_("14.29"), Mxn_("14.29"),
        ]);
    }

    [Fact]
    public void SplitEqually_ExactDivision_NoAdjustmentNeeded()
    {
        var shares = SplitCalculator.SplitEqually(Mxn_("500"), participantCount: 4);

        shares.ShouldAllBe(s => s == Mxn_("125"));
    }

    [Fact]
    public void SplitEqually_SingleParticipant_GetsWholeTotal()
    {
        var shares = SplitCalculator.SplitEqually(Mxn_("129.50"), participantCount: 1);

        shares.ShouldBe([Mxn_("129.50")]);
    }

    [Fact]
    public void SplitEqually_ThirdDecimalExactlyFive_RoundsUp()
    {
        // 0.01 / 2 = 0.005 → 0.01 each = 0.02 → −0.01 to position 1 → [0.00, 0.01]
        var shares = SplitCalculator.SplitEqually(Mxn_("0.01"), participantCount: 2);

        shares.ShouldBe([Mxn_("0.00"), Mxn_("0.01")]);
    }

    [Fact]
    public void SplitEqually_TinyTotal_ThirdDecimalBelowFive_RoundsDown()
    {
        // 0.01 / 3 = 0.0033… → 0.00 each → +0.01 to position 1 → [0.01, 0, 0]
        var shares = SplitCalculator.SplitEqually(Mxn_("0.01"), participantCount: 3);

        shares.ShouldBe([Mxn_("0.01"), Mxn_("0.00"), Mxn_("0.00")]);
    }

    [Fact]
    public void SplitEqually_AdjustmentWouldMakeFirstShareNegative_ThrowsInvalidSplit()
    {
        // 0.50 / 100 = 0.005 → 0.01 each = 1.00 → −0.50 to position 1 → negative: not allowed
        Should.Throw<InvalidSplitException>(() => SplitCalculator.SplitEqually(Mxn_("0.50"), participantCount: 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SplitEqually_NonPositiveParticipantCount_ThrowsInvalidSplit(int count)
    {
        Should.Throw<InvalidSplitException>(() => SplitCalculator.SplitEqually(Mxn_("100"), count));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void SplitEqually_NonPositiveTotal_ThrowsInvalidSplit(string total)
    {
        Should.Throw<InvalidSplitException>(() => SplitCalculator.SplitEqually(Mxn_(total), participantCount: 2));
    }

    [Fact]
    public void SplitEqually_ResultCurrency_MatchesTotalCurrency()
    {
        var usd = new Currency("USD", 2);

        var shares = SplitCalculator.SplitEqually(Money.Of(10m, usd), participantCount: 3);

        shares.ShouldAllBe(s => s.Currency == usd);
    }

    /// <summary>
    /// Property-style check over many deterministic pseudo-random cases: the shares always add
    /// up to the total, no share is negative, every share except the first equals the rounded
    /// base, and the first share deviates from the base by at most 0.005 per participant.
    /// </summary>
    [Theory]
    [MemberData(nameof(RandomCases))]
    public void SplitEqually_Invariants_HoldForRandomInputs(decimal total, int count)
    {
        var money = Money.Of(total, Mxn);

        IReadOnlyList<Money> shares;
        try
        {
            shares = SplitCalculator.SplitEqually(money, count);
        }
        catch (InvalidSplitException)
        {
            // Only allowed when the first share would be negative under the team rule.
            decimal expectedBase = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
            (expectedBase + (total - (expectedBase * count))).ShouldBeLessThan(0m);
            return;
        }

        shares.Count.ShouldBe(count);
        shares.Sum(s => s.Amount).ShouldBe(total);
        shares.ShouldAllBe(s => s.Amount >= 0m);

        decimal baseShare = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
        shares.Skip(1).ShouldAllBe(s => s.Amount == baseShare);
        Math.Abs(shares[0].Amount - baseShare).ShouldBeLessThanOrEqualTo(0.005m * count);
    }

    public static TheoryData<decimal, int> RandomCases()
    {
        var data = new TheoryData<decimal, int>();
        var random = new Random(Seed: 20261115);
        for (int i = 0; i < 1_000; i++)
        {
            decimal total = random.Next(1, 1_000_000) / 100m;      // 0.01 .. 9,999.99
            int count = random.Next(1, 51);                        // 1 .. 50
            data.Add(total, count);
        }

        return data;
    }

    // --------------------------------------------------------------- CUSTOM

    [Fact]
    public void ValidateCustomShares_SharesSumToTotal_Passes()
    {
        // Team example: 500 = 200 + 129.50 + 170.50
        Should.NotThrow(() => SplitCalculator.ValidateCustomShares(
            Mxn_("500"), [Mxn_("200"), Mxn_("129.50"), Mxn_("170.50")]));
    }

    [Fact]
    public void ValidateCustomShares_SharesDoNotSumToTotal_ThrowsInvalidSplit()
    {
        var ex = Should.Throw<InvalidSplitException>(() => SplitCalculator.ValidateCustomShares(
            Mxn_("500"), [Mxn_("200"), Mxn_("129.50"), Mxn_("170.00")]));

        ex.Message.ShouldContain("499.50");
        ex.Message.ShouldContain("500.00");
    }

    [Fact]
    public void ValidateCustomShares_NegativeShare_ThrowsInvalidSplit()
    {
        Should.Throw<InvalidSplitException>(() => SplitCalculator.ValidateCustomShares(
            Mxn_("100"), [Mxn_("150"), Mxn_("-50")]));
    }

    [Fact]
    public void ValidateCustomShares_ZeroShareIsAllowed()
    {
        Should.NotThrow(() => SplitCalculator.ValidateCustomShares(
            Mxn_("100"), [Mxn_("100"), Mxn_("0")]));
    }

    [Fact]
    public void ValidateCustomShares_EmptyShares_ThrowsInvalidSplit()
    {
        Should.Throw<InvalidSplitException>(() => SplitCalculator.ValidateCustomShares(Mxn_("100"), []));
    }

    [Fact]
    public void ValidateCustomShares_ShareInOtherCurrency_ThrowsCurrencyMismatch()
    {
        var usd = Money.Of(100m, new Currency("USD", 2));

        Should.Throw<CurrencyMismatchException>(() => SplitCalculator.ValidateCustomShares(Mxn_("100"), [usd]));
    }
}
