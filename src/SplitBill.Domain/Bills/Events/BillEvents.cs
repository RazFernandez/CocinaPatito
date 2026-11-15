using SplitBill.Domain.Common;
using SplitBill.Domain.Monetary;

namespace SplitBill.Domain.Bills.Events;

/// <summary>A bill was created with its participants and shares.</summary>
public sealed record BillCreated(Guid BillId, Guid OwnerUserId, Money Total, int ParticipantCount, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>A participant's charge was settled by a CoDi payment.</summary>
public sealed record ParticipantPaid(Guid BillId, Guid ParticipantId, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>Every chargeable participant has paid.</summary>
public sealed record BillSettled(Guid BillId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>The owner cancelled the bill; live QRs must be discarded.</summary>
public sealed record BillCancelled(Guid BillId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>
/// A payment arrived that does not match an open charge (duplicate, wrong amount, closed bill).
/// Triggers manual reconciliation; nothing is charged twice.
/// </summary>
public sealed record UnexpectedPaymentReceived(Guid BillId, Guid ParticipantId, Money Amount, PaymentOutcome Reason, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>A CoDi QR was issued for a participant and will expire at <see cref="ExpiresAt"/>.</summary>
public sealed record CodiQrIssued(Guid BillId, Guid ParticipantId, Guid CodiQrId, DateTimeOffset ExpiresAt, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>A CoDi QR reached its 20-minute lifetime without payment and was deleted.</summary>
public sealed record CodiQrExpired(Guid BillId, Guid ParticipantId, Guid CodiQrId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}

/// <summary>A CoDi QR was consumed by a payment and deleted.</summary>
public sealed record CodiQrConsumed(Guid BillId, Guid ParticipantId, Guid CodiQrId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid AggregateId => BillId;
}
