using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;

namespace SplitBill.Domain.Splitting;

/// <summary>
/// Pure arithmetic for splitting a bill. Implements the team rule for equal splits:
/// round <c>total / n</c> to the currency scale (third decimal below 5 rounds down, 5 or above
/// rounds up) and let the participant in position 1 absorb the cent difference so the shares
/// always add up to the total.
/// </summary>
/// <example>
/// <code>
/// SplitCalculator.SplitEqually(Money.Of(100m, Currency.Mxn), 3); // [33.34, 33.33, 33.33]
/// SplitCalculator.SplitEqually(Money.Of(100m, Currency.Mxn), 7); // [14.26, 14.29 × 6]
/// SplitCalculator.ValidateCustomShares(total, shares);           // throws if Σ shares ≠ total
/// </code>
/// </example>
public static class SplitCalculator
{
    /// <summary>
    /// Splits <paramref name="total"/> among <paramref name="participantCount"/> participants.
    /// Index 0 of the result corresponds to position 1 and carries the rounding adjustment.
    /// </summary>
    /// <exception cref="InvalidSplitException">
    /// The count is not positive, the total is not positive, or the adjustment would make the
    /// first share negative (total too small for that many participants under this rule).
    /// </exception>
    public static IReadOnlyList<Money> SplitEqually(Money total, int participantCount)
    {
        if (participantCount <= 0)
        {
            throw new InvalidSplitException("A bill needs at least one participant to split.");
        }

        if (!total.IsPositive)
        {
            throw new InvalidSplitException("The total to split must be greater than zero.");
        }

        Currency currency = total.Currency;
        decimal raw = total.Amount / participantCount;
        decimal baseShare = decimal.Round(raw, currency.MinorUnit, MidpointRounding.AwayFromZero);
        decimal delta = total.Amount - (baseShare * participantCount);
        decimal firstShare = baseShare + delta;

        if (firstShare < 0m)
        {
            throw new InvalidSplitException(
                $"{total} cannot be split equally among {participantCount} participants without a negative share.");
        }

        var shares = new Money[participantCount];
        shares[0] = Money.Of(firstShare, currency);
        for (int i = 1; i < participantCount; i++)
        {
            shares[i] = Money.Of(baseShare, currency);
        }

        return shares;
    }

    /// <summary>
    /// Validates client-provided shares for a CUSTOM split: at least one share, none negative,
    /// all in the total's currency, and summing exactly to the total.
    /// </summary>
    /// <exception cref="InvalidSplitException">The shares are empty, negative or do not balance.</exception>
    /// <exception cref="CurrencyMismatchException">A share is in a different currency.</exception>
    public static void ValidateCustomShares(Money total, IReadOnlyList<Money> shares)
    {
        if (shares.Count == 0)
        {
            throw new InvalidSplitException("A custom split needs at least one share.");
        }

        Money sum = Money.Zero(total.Currency);
        foreach (Money share in shares)
        {
            if (share.IsNegative)
            {
                throw new InvalidSplitException("Shares cannot be negative.");
            }

            sum += share; // throws CurrencyMismatchException on a foreign currency
        }

        if (sum != total)
        {
            throw new InvalidSplitException($"Shares add up to {sum} but the bill total is {total}.");
        }
    }
}
