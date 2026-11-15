using Shouldly;
using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;

namespace SplitBill.Domain.Tests.Monetary;

public sealed class MoneyTests
{
    private static readonly Currency Mxn = Currency.Mxn;

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("129.50")]
    [InlineData("170.5")]
    [InlineData("999999999999.99")]
    public void Of_AmountWithAtMostTwoDecimals_CreatesMoney(string raw)
    {
        decimal amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        var money = Money.Of(amount, Mxn);

        money.Amount.ShouldBe(amount);
        money.Currency.ShouldBe(Mxn);
    }

    [Theory]
    [InlineData("33.333")]
    [InlineData("14.285")]
    [InlineData("0.001")]
    public void Of_AmountWithMoreDecimalsThanCurrencyMinorUnit_ThrowsInvalidMoney(string raw)
    {
        decimal amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        Should.Throw<InvalidMoneyException>(() => Money.Of(amount, Mxn));
    }

    [Fact]
    public void Of_TrailingZerosBeyondScale_AreNormalisedToCurrencyScale()
    {
        var money = Money.Of(10.5000m, Mxn);

        ((int)money.Amount.Scale).ShouldBe(2);
        money.Amount.ShouldBe(10.50m);
    }

    [Fact]
    public void Add_SameCurrency_SumsAmounts()
    {
        var a = Money.Of(200m, Mxn);
        var b = Money.Of(129.50m, Mxn);

        (a + b).ShouldBe(Money.Of(329.50m, Mxn));
    }

    [Fact]
    public void Subtract_SameCurrency_SubtractsAmounts()
    {
        var a = Money.Of(500m, Mxn);
        var b = Money.Of(329.50m, Mxn);

        (a - b).ShouldBe(Money.Of(170.50m, Mxn));
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsCurrencyMismatch()
    {
        var pesos = Money.Of(1m, Mxn);
        var dollars = Money.Of(1m, new Currency("USD", 2));

        Should.Throw<CurrencyMismatchException>(() => _ = pesos + dollars);
    }

    [Fact]
    public void Equality_IsByAmountAndCurrency()
    {
        Money.Of(10m, Mxn).ShouldBe(Money.Of(10.00m, Mxn));
        Money.Of(10m, Mxn).ShouldNotBe(Money.Of(10.01m, Mxn));
        Money.Of(10m, Mxn).ShouldNotBe(Money.Of(10m, new Currency("USD", 2)));
    }

    [Fact]
    public void Comparison_OrdersByAmountWithinSameCurrency()
    {
        var small = Money.Of(1m, Mxn);
        var big = Money.Of(2m, Mxn);

        (small < big).ShouldBeTrue();
        (big > small).ShouldBeTrue();
        (small <= Money.Of(1m, Mxn)).ShouldBeTrue();
    }

    [Fact]
    public void Zero_HasZeroAmountInGivenCurrency()
    {
        var zero = Money.Zero(Mxn);

        zero.Amount.ShouldBe(0m);
        zero.IsZero.ShouldBeTrue();
        zero.IsNegative.ShouldBeFalse();
    }

    [Fact]
    public void ToString_UsesInvariantFormatWithCurrencyCode()
    {
        Money.Of(33.34m, Mxn).ToString().ShouldBe("33.34 MXN");
    }
}
