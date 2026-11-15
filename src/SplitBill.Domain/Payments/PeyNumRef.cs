using System.Globalization;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Domain.Payments;

/// <summary>
/// Banxico reference number sent to Pey as <c>numRef</c>: one to seven digits, stored
/// left-padded to seven. Informational for the payer's bank; uniqueness is not required.
/// </summary>
/// <example>
/// <code>
/// PeyNumRef.Parse("9423").Value;          // "0009423"
/// PeyNumRef.FromSequence(123456789).Value; // "3456789" (last seven digits)
/// </code>
/// </example>
public readonly record struct PeyNumRef
{
    /// <summary>Maximum number of digits accepted by Pey.</summary>
    public const int MaxDigits = 7;

    private const long Modulus = 10_000_000;

    private PeyNumRef(string value) => Value = value;

    /// <summary>Seven-digit reference, left-padded with zeros.</summary>
    public string Value { get; }

    /// <summary>Parses a reference of one to seven digits.</summary>
    /// <exception cref="BillRuleException">The text is empty, longer than seven characters or not all digits.</exception>
    public static PeyNumRef Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxDigits || !raw.All(char.IsAsciiDigit))
        {
            throw new BillRuleException("Pey reference number must be one to seven digits.");
        }

        return new PeyNumRef(raw.PadLeft(MaxDigits, '0'));
    }

    /// <summary>Builds a reference from a non-negative counter, keeping its last seven digits.</summary>
    /// <exception cref="BillRuleException">The sequence is negative.</exception>
    public static PeyNumRef FromSequence(long sequence)
    {
        if (sequence < 0)
        {
            throw new BillRuleException("Pey reference sequence cannot be negative.");
        }

        return new PeyNumRef((sequence % Modulus).ToString("D7", CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
