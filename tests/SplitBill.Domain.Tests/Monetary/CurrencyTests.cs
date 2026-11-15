using Shouldly;
using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;

namespace SplitBill.Domain.Tests.Monetary;

public sealed class CurrencyTests
{
    [Fact]
    public void Mxn_IsIso4217CodeWithTwoDecimals()
    {
        Currency.Mxn.Code.ShouldBe("MXN");
        Currency.Mxn.MinorUnit.ShouldBe(2);
    }

    [Theory]
    [InlineData("mxn")]
    [InlineData("MX")]
    [InlineData("MXNN")]
    [InlineData("M1N")]
    [InlineData("")]
    public void Constructor_InvalidIsoCode_ThrowsInvalidCurrency(string code)
    {
        Should.Throw<InvalidCurrencyException>(() => new Currency(code, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Constructor_MinorUnitOutOfRange_ThrowsInvalidCurrency(int minorUnit)
    {
        Should.Throw<InvalidCurrencyException>(() => new Currency("USD", minorUnit));
    }

    [Fact]
    public void Equality_IsByCode()
    {
        new Currency("MXN", 2).ShouldBe(Currency.Mxn);
    }
}
