namespace SplitBill.Domain.Exceptions;

/// <summary>
/// Base type for every rule violation raised by the domain. Adapters map it to a 4xx response;
/// it never carries personal data in its message.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>Creates the exception with a message safe to show to API clients.</summary>
    protected DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Required by analyzers; domain exceptions always carry a message.</summary>
    protected DomainException()
    {
    }
}

/// <summary>Raised when a monetary amount does not fit the currency scale.</summary>
public sealed class InvalidMoneyException : DomainException
{
    /// <inheritdoc cref="InvalidMoneyException"/>
    public InvalidMoneyException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="InvalidMoneyException"/>
    public InvalidMoneyException()
    {
    }

    /// <inheritdoc cref="InvalidMoneyException"/>
    public InvalidMoneyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when two amounts in different currencies are combined.</summary>
public sealed class CurrencyMismatchException : DomainException
{
    /// <inheritdoc cref="CurrencyMismatchException"/>
    public CurrencyMismatchException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="CurrencyMismatchException"/>
    public CurrencyMismatchException()
    {
    }

    /// <inheritdoc cref="CurrencyMismatchException"/>
    public CurrencyMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when a currency code or minor unit is not valid.</summary>
public sealed class InvalidCurrencyException : DomainException
{
    /// <inheritdoc cref="InvalidCurrencyException"/>
    public InvalidCurrencyException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="InvalidCurrencyException"/>
    public InvalidCurrencyException()
    {
    }

    /// <inheritdoc cref="InvalidCurrencyException"/>
    public InvalidCurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when a split cannot be computed or the given shares do not balance.</summary>
public sealed class InvalidSplitException : DomainException
{
    /// <inheritdoc cref="InvalidSplitException"/>
    public InvalidSplitException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="InvalidSplitException"/>
    public InvalidSplitException()
    {
    }

    /// <inheritdoc cref="InvalidSplitException"/>
    public InvalidSplitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when input violates a structural rule of a bill (participants, title, identifiers).</summary>
public sealed class BillRuleException : DomainException
{
    /// <inheritdoc cref="BillRuleException"/>
    public BillRuleException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="BillRuleException"/>
    public BillRuleException()
    {
    }

    /// <inheritdoc cref="BillRuleException"/>
    public BillRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when an operation is not allowed in the current state of the bill or participant.</summary>
public sealed class BillStateException : DomainException
{
    /// <inheritdoc cref="BillStateException"/>
    public BillStateException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="BillStateException"/>
    public BillStateException()
    {
    }

    /// <inheritdoc cref="BillStateException"/>
    public BillStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
