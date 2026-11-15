using System.Text.RegularExpressions;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Domain.Monetary;

/// <summary>
/// ISO 4217 currency. Equality is by <see cref="Code"/> only; <see cref="MinorUnit"/> is the
/// number of decimals the currency uses (2 for MXN). The catalogue lives in the database
/// (<c>currency</c> table); the domain only needs the code and the scale.
/// </summary>
/// <example>
/// <code>
/// var mxn = Currency.Mxn;                 // "MXN", 2 decimals
/// var jpy = new Currency("JPY", 0);       // future extension, no schema change
/// </code>
/// </example>
public readonly partial record struct Currency
{
    /// <summary>Mexican peso, the only currency active today.</summary>
    public static readonly Currency Mxn = new("MXN", 2);

    /// <summary>Creates a currency after validating the ISO code and the minor unit (0..4).</summary>
    /// <exception cref="InvalidCurrencyException">Code is not three upper-case letters or the scale is out of range.</exception>
    public Currency(string code, int minorUnit)
    {
        if (code is null || !IsoCodePattern().IsMatch(code))
        {
            throw new InvalidCurrencyException("Currency code must be exactly three upper-case letters (ISO 4217).");
        }

        if (minorUnit is < 0 or > 4)
        {
            throw new InvalidCurrencyException("Currency minor unit must be between 0 and 4 decimals.");
        }

        Code = code;
        MinorUnit = minorUnit;
    }

    /// <summary>Three-letter ISO 4217 code, e.g. <c>MXN</c>.</summary>
    public string Code { get; }

    /// <summary>Number of decimal places used by the currency.</summary>
    public int MinorUnit { get; }

    /// <inheritdoc/>
    public bool Equals(Currency other) => string.Equals(Code, other.Code, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code ?? string.Empty);

    /// <inheritdoc/>
    public override string ToString() => Code;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex IsoCodePattern();
}
