namespace SplitBill.Domain.Bills;

/// <summary>Lifecycle of a bill. Partial progress is derived from participants, never stored.</summary>
public enum BillStatus
{
    /// <summary>Charges can be issued and payments accepted.</summary>
    Open = 1,

    /// <summary>Every chargeable participant has paid.</summary>
    Settled = 2,

    /// <summary>Closed by the owner; no further charges or payments are expected.</summary>
    Cancelled = 3,
}

/// <summary>State of a participant's charge.</summary>
public enum ParticipantStatus
{
    /// <summary>Waiting for a CoDi payment.</summary>
    Pending = 1,

    /// <summary>Paid in full through CoDi.</summary>
    Paid = 2,

    /// <summary>Not charged (the owner already covered the bill).</summary>
    Waived = 3,
}

/// <summary>
/// Result of registering a payment reported by Pey. Anything other than
/// <see cref="Accepted"/> is stored with <c>is_unexpected = true</c> for manual reconciliation.
/// </summary>
public enum PaymentOutcome
{
    /// <summary>The payment matched an open charge and settled it.</summary>
    Accepted = 1,

    /// <summary>The participant was already paid (same QR scanned twice).</summary>
    UnexpectedAlreadyPaid = 2,

    /// <summary>The participant is not chargeable (owner share).</summary>
    UnexpectedParticipantNotPending = 3,

    /// <summary>The amount or currency differs from the participant's share.</summary>
    UnexpectedAmountMismatch = 4,

    /// <summary>The bill is settled or cancelled.</summary>
    UnexpectedBillNotOpen = 5,
}
