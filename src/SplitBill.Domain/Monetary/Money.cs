using System.Globalization;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Domain.Monetary;

/// <summary>
/// Exact monetary amount in a given <see cref="Currency"/>. Always a <see cref="decimal"/>
/// normalised to the currency scale; never a floating-point number. Arithmetic across
/// currencies is rejected.
/// </summary>
/// <example>
/// <code>
/// var share = Money.Of(33.34m, Currency.Mxn);
/// var total = share + Money.Of(66.66m, Currency.Mxn);   // 100.00 MXN
/// Money.Of(33.333m, Currency.Mxn);                      // throws InvalidMoneyException
/// </code>
/// </example>
public readonly record struct Money : IComparable<Money>
{
    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Amount normalised to the currency scale (e.g. two decimals for MXN).</summary>
    public decimal Amount { get; }

    /// <summary>Currency of the amount.</summary>
    public Currency Currency { get; }

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True when the amount is below zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>True when the amount is above zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>
    /// Creates an amount, rejecting values with more decimals than the currency allows.
    /// Trailing zeros beyond the scale are normalised away (10.5000 → 10.50).
    /// </summary>
    /// <exception cref="InvalidMoneyException">The amount has more decimals than <see cref="Currency.MinorUnit"/>.</exception>
    public static Money Of(decimal amount, Currency currency)
    {
        decimal normalised = decimal.Round(amount, currency.MinorUnit, MidpointRounding.AwayFromZero);
        if (normalised != amount)
        {
            throw new InvalidMoneyException(
                $"Amount {amount.ToString(CultureInfo.InvariantCulture)} has more than {currency.MinorUnit} decimals for {currency.Code}.");
        }

        return new Money(normalised, currency);
    }

    /// <summary>Zero amount in the given currency.</summary>
    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>Adds two amounts of the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtracts two amounts of the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>Less-than comparison within the same currency.</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than comparison within the same currency.</summary>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal comparison within the same currency.</summary>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal comparison within the same currency.</summary>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Named equivalent of <c>+</c>.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Named equivalent of <c>-</c>.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies the amount by an integer factor (used to check split sums).</summary>
    public Money Multiply(int factor) => new(Amount * factor, Currency);

    /// <inheritdoc/>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>Invariant representation such as <c>33.34 MXN</c>; used in messages and logs.</summary>
    public override string ToString() =>
        Amount.ToString("F" + Currency.MinorUnit.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
        + " " + Currency.Code;

    private void EnsureSameCurrency(Money other)
    {
        if (!Currency.Equals(other.Currency))
        {
            throw new CurrencyMismatchException($"Cannot combine {Currency.Code} with {other.Currency.Code}.");
        }
    }
}
