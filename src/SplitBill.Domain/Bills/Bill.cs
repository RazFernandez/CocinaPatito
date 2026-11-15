using SplitBill.Domain.Bills.Events;
using SplitBill.Domain.Common;
using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;
using SplitBill.Domain.Payments;
using SplitBill.Domain.Splitting;

namespace SplitBill.Domain.Bills;

/// <summary>
/// Aggregate root for a shared bill: the total, how it is split, the participants and their
/// charges. All state transitions (issue QR, register payment, cancel) go through this class so
/// the invariants hold: shares always add up to the total, the owner is never charged, a
/// participant is paid at most once, and the bill settles exactly when everyone is covered.
/// </summary>
/// <example>
/// <code>
/// var bill = Bill.CreateEqualSplit(ownerId, bankAccountId, "Tacos", Money.Of(100m, Currency.Mxn),
///     [new("Ana", anaId, IsOwner: true), new("Beto", null, false), new("Caro", null, false)], clock.UtcNow);
/// var qr = bill.IssueCodiQr(betoId, pey.Id, pey.QrString, PeyNumRef.FromSequence(seq), clock.UtcNow);
/// var outcome = bill.RegisterPayment(betoId, Money.Of(33.33m, Currency.Mxn), paidAt); // Accepted
/// </code>
/// </example>
public sealed class Bill : AggregateRoot
{
    /// <summary>Upper bound on participants per bill; keeps QR issuance and screens manageable.</summary>
    public const int MaxParticipants = 100;

    /// <summary>Maximum length of the bill title.</summary>
    public const int MaxTitleLength = 120;

    private readonly List<BillParticipant> _participants;

    private Bill(
        Guid id,
        Guid ownerUserId,
        Guid receivingBankAccountId,
        string title,
        Money total,
        SplitMethod splitMethod,
        BillStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? settledAt,
        DateTimeOffset? cancelledAt,
        List<BillParticipant> participants)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        ReceivingBankAccountId = receivingBankAccountId;
        Title = title;
        Total = total;
        SplitMethod = splitMethod;
        Status = status;
        CreatedAt = createdAt;
        SettledAt = settledAt;
        CancelledAt = cancelledAt;
        _participants = participants;
    }

    /// <summary>Bill identifier (UUID v7).</summary>
    public Guid Id { get; }

    /// <summary>User who paid the bill and receives the CoDi transfers.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>Owner bank account (registered at Pey) that receives the money.</summary>
    public Guid ReceivingBankAccountId { get; }

    /// <summary>Short description shown to participants.</summary>
    public string Title { get; }

    /// <summary>Amount to split.</summary>
    public Money Total { get; }

    /// <summary>How the total was distributed.</summary>
    public SplitMethod SplitMethod { get; }

    /// <summary>Lifecycle state.</summary>
    public BillStatus Status { get; private set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the last charge was paid, if settled.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>When the owner cancelled, if cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Participants ordered by position.</summary>
    public IReadOnlyList<BillParticipant> Participants => _participants.AsReadOnly();

    /// <summary>Sum of shares already paid or waived. Derived, never stored.</summary>
    public Money CoveredAmount =>
        _participants.Where(p => p.IsCovered).Aggregate(Money.Zero(Total.Currency), (acc, p) => acc + p.Share);

    /// <summary>What is still missing to settle the bill. Derived, never stored.</summary>
    public Money RemainingAmount => Total - CoveredAmount;

    /// <summary>
    /// Creates a bill whose total is divided equally; the first draft (position 1) absorbs the
    /// cent adjustment. Raises <see cref="BillCreated"/>.
    /// </summary>
    /// <exception cref="BillRuleException">Title or participants violate a structural rule.</exception>
    /// <exception cref="InvalidSplitException">The total cannot be split among that many participants.</exception>
    public static Bill CreateEqualSplit(
        Guid ownerUserId,
        Guid receivingBankAccountId,
        string title,
        Money total,
        IReadOnlyList<ParticipantDraft> drafts,
        DateTimeOffset now)
    {
        ValidateDrafts(drafts);
        IReadOnlyList<Money> shares = SplitCalculator.SplitEqually(total, drafts.Count);
        return Create(ownerUserId, receivingBankAccountId, title, total, SplitMethod.Equal, drafts, shares, now);
    }

    /// <summary>
    /// Creates a bill with explicit shares, one per draft in the same order. The shares must add
    /// up to the total. Raises <see cref="BillCreated"/>.
    /// </summary>
    /// <exception cref="BillRuleException">Title or participants violate a structural rule.</exception>
    /// <exception cref="InvalidSplitException">Share count differs from drafts or shares do not balance.</exception>
    public static Bill CreateCustomSplit(
        Guid ownerUserId,
        Guid receivingBankAccountId,
        string title,
        Money total,
        IReadOnlyList<ParticipantDraft> drafts,
        IReadOnlyList<Money> shares,
        DateTimeOffset now)
    {
        ValidateDrafts(drafts);
        if (shares.Count != drafts.Count)
        {
            throw new InvalidSplitException($"Expected {drafts.Count} shares, received {shares.Count}.");
        }

        SplitCalculator.ValidateCustomShares(total, shares);
        return Create(ownerUserId, receivingBankAccountId, title, total, SplitMethod.Custom, drafts, shares, now);
    }

    /// <summary>Rebuilds a bill from persisted state. Used only by repositories; raises no events.</summary>
    public static Bill Rehydrate(
        Guid id,
        Guid ownerUserId,
        Guid receivingBankAccountId,
        string title,
        Money total,
        SplitMethod splitMethod,
        BillStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? settledAt,
        DateTimeOffset? cancelledAt,
        IEnumerable<BillParticipant> participants) =>
        new(id, ownerUserId, receivingBankAccountId, title, total, splitMethod, status, createdAt, settledAt, cancelledAt,
            participants.OrderBy(p => p.Position).ToList());

    /// <summary>
    /// Issues a CoDi QR for a pending participant using the identifiers returned by Pey.
    /// Raises <see cref="CodiQrIssued"/>. The caller persists the QR and deletes any expired one.
    /// </summary>
    /// <exception cref="BillStateException">The bill is not open or the participant is not pending.</exception>
    /// <exception cref="BillRuleException">Unknown participant or blank Pey identifiers.</exception>
    public CodiQr IssueCodiQr(Guid participantId, string peyCodiQrId, string qrString, PeyNumRef numRef, DateTimeOffset now)
    {
        if (Status != BillStatus.Open)
        {
            throw new BillStateException($"Cannot issue a QR for a {Status} bill.");
        }

        BillParticipant participant = FindParticipant(participantId);
        if (participant.Status != ParticipantStatus.Pending)
        {
            throw new BillStateException($"Participant is {participant.Status}; only pending participants can be charged.");
        }

        if (string.IsNullOrWhiteSpace(peyCodiQrId) || string.IsNullOrWhiteSpace(qrString))
        {
            throw new BillRuleException("Pey QR identifier and QR string are required.");
        }

        CodiQr qr = CodiQr.Issue(participant.Id, peyCodiQrId, qrString, participant.Share, numRef, now);
        Raise(new CodiQrIssued(Id, participant.Id, qr.Id, qr.ExpiresAt, now));
        return qr;
    }

    /// <summary>
    /// Applies a payment confirmed by Pey. An exact match on a pending participant settles the
    /// charge (and the bill when everyone is covered); anything else is reported as unexpected
    /// and leaves the state untouched so it can be reconciled manually.
    /// </summary>
    /// <exception cref="BillRuleException">The participant does not belong to this bill.</exception>
    public PaymentOutcome RegisterPayment(Guid participantId, Money amount, DateTimeOffset paidAt)
    {
        BillParticipant participant = FindParticipant(participantId);

        PaymentOutcome outcome = Evaluate(participant, amount);
        if (outcome != PaymentOutcome.Accepted)
        {
            Raise(new UnexpectedPaymentReceived(Id, participant.Id, amount, outcome, paidAt));
            return outcome;
        }

        participant.MarkPaid(paidAt);
        Raise(new ParticipantPaid(Id, participant.Id, amount, paidAt));

        if (_participants.All(p => p.IsCovered))
        {
            Status = BillStatus.Settled;
            SettledAt = paidAt;
            Raise(new BillSettled(Id, paidAt));
        }

        return PaymentOutcome.Accepted;
    }

    /// <summary>Cancels an open bill. Raises <see cref="BillCancelled"/>; live QRs must then be deleted.</summary>
    /// <exception cref="BillStateException">The bill is already settled or cancelled.</exception>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != BillStatus.Open)
        {
            throw new BillStateException($"Cannot cancel a {Status} bill.");
        }

        Status = BillStatus.Cancelled;
        CancelledAt = now;
        Raise(new BillCancelled(Id, now));
    }

    private static Bill Create(
        Guid ownerUserId,
        Guid receivingBankAccountId,
        string title,
        Money total,
        SplitMethod splitMethod,
        IReadOnlyList<ParticipantDraft> drafts,
        IReadOnlyList<Money> shares,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            throw new BillRuleException($"Title is required and must be at most {MaxTitleLength} characters.");
        }

        var billId = Guid.CreateVersion7();
        var participants = new List<BillParticipant>(drafts.Count);
        for (int i = 0; i < drafts.Count; i++)
        {
            participants.Add(BillParticipant.Create(billId, drafts[i], position: i + 1, shares[i]));
        }

        var bill = new Bill(billId, ownerUserId, receivingBankAccountId, title.Trim(), total, splitMethod,
            BillStatus.Open, now, settledAt: null, cancelledAt: null, participants);
        bill.Raise(new BillCreated(billId, ownerUserId, total, participants.Count, now));
        return bill;
    }

    private static void ValidateDrafts(IReadOnlyList<ParticipantDraft> drafts)
    {
        if (drafts.Count is 0 or > MaxParticipants)
        {
            throw new BillRuleException($"A bill needs between 1 and {MaxParticipants} participants.");
        }

        if (drafts.Any(d => string.IsNullOrWhiteSpace(d.DisplayName)))
        {
            throw new BillRuleException("Every participant needs a display name.");
        }

        if (drafts.Count(d => d.IsOwner) > 1)
        {
            throw new BillRuleException("A bill can have at most one owner participant.");
        }

        if (drafts.All(d => d.IsOwner))
        {
            throw new BillRuleException("A bill needs at least one participant to charge.");
        }

        var registered = drafts.Where(d => d.UserId is not null).Select(d => d.UserId!.Value).ToList();
        if (registered.Distinct().Count() != registered.Count)
        {
            throw new BillRuleException("A registered user cannot appear twice in the same bill.");
        }
    }

    private BillParticipant FindParticipant(Guid participantId) =>
        _participants.Find(p => p.Id == participantId)
        ?? throw new BillRuleException("Participant does not belong to this bill.");

    private PaymentOutcome Evaluate(BillParticipant participant, Money amount)
    {
        if (Status != BillStatus.Open)
        {
            return PaymentOutcome.UnexpectedBillNotOpen;
        }

        if (participant.Status == ParticipantStatus.Paid)
        {
            return PaymentOutcome.UnexpectedAlreadyPaid;
        }

        if (participant.Status != ParticipantStatus.Pending)
        {
            return PaymentOutcome.UnexpectedParticipantNotPending;
        }

        return amount == participant.Share ? PaymentOutcome.Accepted : PaymentOutcome.UnexpectedAmountMismatch;
    }
}
