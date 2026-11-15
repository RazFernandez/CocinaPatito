using SplitBill.Domain.Monetary;

namespace SplitBill.Domain.Bills;

/// <summary>
/// A person among whom the bill is split, together with their charge. One row in
/// <c>bill_participant</c>; state changes only happen through the owning <see cref="Bill"/>.
/// </summary>
public sealed class BillParticipant
{
    private BillParticipant(
        Guid id,
        Guid billId,
        Guid? userId,
        string displayName,
        int position,
        Money share,
        bool isOwner,
        ParticipantStatus status,
        DateTimeOffset? paidAt)
    {
        Id = id;
        BillId = billId;
        UserId = userId;
        DisplayName = displayName;
        Position = position;
        Share = share;
        IsOwner = isOwner;
        Status = status;
        PaidAt = paidAt;
    }

    /// <summary>Participant identifier (UUID v7).</summary>
    public Guid Id { get; }

    /// <summary>Owning bill.</summary>
    public Guid BillId { get; }

    /// <summary>Registered user, or <see langword="null"/> for a guest.</summary>
    public Guid? UserId { get; }

    /// <summary>Name shown on the bill screen.</summary>
    public string DisplayName { get; private set; }

    /// <summary>1-based position; position 1 absorbs the cent adjustment.</summary>
    public int Position { get; }

    /// <summary>Amount this participant owes.</summary>
    public Money Share { get; }

    /// <summary>True for the bill owner, whose share is never charged.</summary>
    public bool IsOwner { get; }

    /// <summary>Current charge state.</summary>
    public ParticipantStatus Status { get; private set; }

    /// <summary>When the CoDi payment was confirmed, if any.</summary>
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>True when the share is covered, either paid or waived.</summary>
    public bool IsCovered => Status is ParticipantStatus.Paid or ParticipantStatus.Waived;

    internal static BillParticipant Create(Guid billId, ParticipantDraft draft, int position, Money share) =>
        new(
            Guid.CreateVersion7(),
            billId,
            draft.UserId,
            draft.DisplayName.Trim(),
            position,
            share,
            draft.IsOwner,
            draft.IsOwner ? ParticipantStatus.Waived : ParticipantStatus.Pending,
            paidAt: null);

    /// <summary>Rebuilds a participant from persisted state. Used only by repositories.</summary>
    public static BillParticipant Rehydrate(
        Guid id,
        Guid billId,
        Guid? userId,
        string displayName,
        int position,
        Money share,
        bool isOwner,
        ParticipantStatus status,
        DateTimeOffset? paidAt) =>
        new(id, billId, userId, displayName, position, share, isOwner, status, paidAt);

    internal void MarkPaid(DateTimeOffset paidAt)
    {
        Status = ParticipantStatus.Paid;
        PaidAt = paidAt;
    }
}
