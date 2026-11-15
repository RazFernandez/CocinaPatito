using SplitBill.Domain.Monetary;
using SplitBill.Domain.Payments;

namespace SplitBill.Domain.Bills;

/// <summary>
/// A live CoDi QR issued at Pey for one participant. Ephemeral by design: the application
/// deletes it when the payment arrives or when <see cref="ExpiresAt"/> passes (20 minutes).
/// The <see cref="QrString"/> is plaintext here; the persistence adapter encrypts it.
/// </summary>
/// <example>
/// <code>
/// var qr = bill.IssueCodiQr(participantId, peyResponse.Id, peyResponse.QrString, numRef, clock.UtcNow);
/// if (qr.IsExpired(clock.UtcNow)) { /* delete and offer a new one */ }
/// </code>
/// </example>
public sealed class CodiQr
{
    /// <summary>How long a QR stays valid. Also sent to Pey as <c>expiration</c> in seconds.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    private CodiQr(
        Guid id,
        Guid participantId,
        string peyCodiQrId,
        string qrString,
        Money amountRequested,
        PeyNumRef numRef,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        ParticipantId = participantId;
        PeyCodiQrId = peyCodiQrId;
        QrString = qrString;
        AmountRequested = amountRequested;
        NumRef = numRef;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>QR identifier (UUID v7).</summary>
    public Guid Id { get; }

    /// <summary>Participant (charge) this QR belongs to. Unique: one live QR per charge.</summary>
    public Guid ParticipantId { get; }

    /// <summary>IDC assigned by Banxico, returned by Pey as <c>id</c>.</summary>
    public string PeyCodiQrId { get; }

    /// <summary>Payload to render as a QR image on the client. Sensitive.</summary>
    public string QrString { get; }

    /// <summary>Amount embedded in the QR; equals the participant's share at issue time.</summary>
    public Money AmountRequested { get; }

    /// <summary>Reference number sent to Pey.</summary>
    public PeyNumRef NumRef { get; }

    /// <summary>Issue time (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Moment after which the QR must be discarded.</summary>
    public DateTimeOffset ExpiresAt { get; }

    internal static CodiQr Issue(Guid participantId, string peyCodiQrId, string qrString, Money amount, PeyNumRef numRef, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), participantId, peyCodiQrId, qrString, amount, numRef, now, now + Lifetime);

    /// <summary>Rebuilds a QR from persisted state. Used only by repositories.</summary>
    public static CodiQr Rehydrate(
        Guid id,
        Guid participantId,
        string peyCodiQrId,
        string qrString,
        Money amountRequested,
        PeyNumRef numRef,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) =>
        new(id, participantId, peyCodiQrId, qrString, amountRequested, numRef, createdAt, expiresAt);

    /// <summary>True once <paramref name="now"/> reaches <see cref="ExpiresAt"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Time left before expiry, never negative.</summary>
    public TimeSpan RemainingLifetime(DateTimeOffset now)
    {
        TimeSpan remaining = ExpiresAt - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
