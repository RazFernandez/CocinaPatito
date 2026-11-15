using System.Globalization;
using Shouldly;
using SplitBill.Domain.Bills;
using SplitBill.Domain.Bills.Events;
using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;
using SplitBill.Domain.Splitting;

namespace SplitBill.Domain.Tests.Bills;

public sealed class BillTests
{
    private static readonly Currency Mxn = Currency.Mxn;
    private static readonly Guid OwnerId = Guid.CreateVersion7();
    private static readonly Guid BankAccountId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static Money Mxn_(string amount) =>
        Money.Of(decimal.Parse(amount, CultureInfo.InvariantCulture), Mxn);

    private static Bill CreateEqualBill(string total, params string[] names)
    {
        var participants = names.Select((n, i) => new ParticipantDraft(n, UserId: null, IsOwner: i == 0)).ToList();
        return Bill.CreateEqualSplit(OwnerId, BankAccountId, "Dinner", Mxn_(total), participants, Now);
    }

    // ------------------------------------------------------------- creation

    [Fact]
    public void CreateEqualSplit_ThreeParticipants_AssignsPositionsAndRoundedShares()
    {
        var bill = CreateEqualBill("100", "Ana", "Beto", "Caro");

        bill.Status.ShouldBe(BillStatus.Open);
        bill.SplitMethod.ShouldBe(SplitMethod.Equal);
        bill.Total.ShouldBe(Mxn_("100"));
        bill.Participants.Select(p => p.Position).ShouldBe([1, 2, 3]);
        bill.Participants.Select(p => p.Share).ShouldBe([Mxn_("33.34"), Mxn_("33.33"), Mxn_("33.33")]);
    }

    [Fact]
    public void CreateEqualSplit_OwnerParticipant_IsWaivedAndNeverCharged()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto", "Caro");

        var owner = bill.Participants.Single(p => p.IsOwner);
        owner.Status.ShouldBe(ParticipantStatus.Waived);
        bill.Participants.Where(p => !p.IsOwner).ShouldAllBe(p => p.Status == ParticipantStatus.Pending);
    }

    [Fact]
    public void CreateEqualSplit_RaisesBillCreatedEvent()
    {
        var bill = CreateEqualBill("100", "Ana", "Beto");

        var created = bill.DomainEvents.OfType<BillCreated>().ShouldHaveSingleItem();
        created.BillId.ShouldBe(bill.Id);
        created.OccurredAt.ShouldBe(Now);
    }

    [Fact]
    public void CreateCustomSplit_SharesSumToTotal_UsesGivenShares()
    {
        var drafts = new List<ParticipantDraft>
        {
            new("Ana", null, IsOwner: false),
            new("Beto", null, IsOwner: false),
            new("Caro", null, IsOwner: false),
        };
        var shares = new[] { Mxn_("200"), Mxn_("129.50"), Mxn_("170.50") };

        var bill = Bill.CreateCustomSplit(OwnerId, BankAccountId, "Dinner", Mxn_("500"), drafts, shares, Now);

        bill.SplitMethod.ShouldBe(SplitMethod.Custom);
        bill.Participants.Select(p => p.Share).ShouldBe(shares);
    }

    [Fact]
    public void CreateCustomSplit_SharesDoNotBalance_ThrowsInvalidSplit()
    {
        var drafts = new List<ParticipantDraft> { new("Ana", null, false), new("Beto", null, false) };

        Should.Throw<InvalidSplitException>(() => Bill.CreateCustomSplit(
            OwnerId, BankAccountId, "Dinner", Mxn_("500"), drafts, [Mxn_("200"), Mxn_("200")], Now));
    }

    [Fact]
    public void CreateCustomSplit_ShareCountDiffersFromParticipantCount_ThrowsInvalidSplit()
    {
        var drafts = new List<ParticipantDraft> { new("Ana", null, false), new("Beto", null, false) };

        Should.Throw<InvalidSplitException>(() => Bill.CreateCustomSplit(
            OwnerId, BankAccountId, "Dinner", Mxn_("500"), drafts, [Mxn_("500")], Now));
    }

    [Fact]
    public void Create_NoChargeableParticipant_ThrowsBillRule()
    {
        // Only the owner: there is nobody to charge.
        Should.Throw<BillRuleException>(() => CreateEqualBill("100", "Ana (owner)"));
    }

    [Fact]
    public void Create_MoreThanOneOwner_ThrowsBillRule()
    {
        var drafts = new List<ParticipantDraft> { new("Ana", null, true), new("Beto", null, true), new("Caro", null, false) };

        Should.Throw<BillRuleException>(() => Bill.CreateEqualSplit(OwnerId, BankAccountId, "Dinner", Mxn_("100"), drafts, Now));
    }

    [Fact]
    public void Create_TooManyParticipants_ThrowsBillRule()
    {
        var names = Enumerable.Range(1, Bill.MaxParticipants + 1).Select(i => $"P{i}").ToArray();

        Should.Throw<BillRuleException>(() => CreateEqualBill("1000", names));
    }

    [Fact]
    public void Create_DuplicateRegisteredUser_ThrowsBillRule()
    {
        var userId = Guid.CreateVersion7();
        var drafts = new List<ParticipantDraft> { new("Ana", userId, false), new("Ana again", userId, false) };

        Should.Throw<BillRuleException>(() => Bill.CreateEqualSplit(OwnerId, BankAccountId, "Dinner", Mxn_("100"), drafts, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankTitle_ThrowsBillRule(string title)
    {
        var drafts = new List<ParticipantDraft> { new("Ana", null, false), new("Beto", null, false) };

        Should.Throw<BillRuleException>(() => Bill.CreateEqualSplit(OwnerId, BankAccountId, title, Mxn_("100"), drafts, Now));
    }

    // ------------------------------------------------------------- payments

    [Fact]
    public void RegisterPayment_ExactShare_MarksParticipantPaidAndRaisesEvent()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto", "Caro");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        var paidAt = Now.AddMinutes(5);

        var outcome = bill.RegisterPayment(beto.Id, beto.Share, paidAt);

        outcome.ShouldBe(PaymentOutcome.Accepted);
        beto.Status.ShouldBe(ParticipantStatus.Paid);
        beto.PaidAt.ShouldBe(paidAt);
        bill.Status.ShouldBe(BillStatus.Open);
        var evt = bill.DomainEvents.OfType<ParticipantPaid>().ShouldHaveSingleItem();
        evt.ParticipantId.ShouldBe(beto.Id);
        evt.BillId.ShouldBe(bill.Id);
    }

    [Fact]
    public void RegisterPayment_LastPendingParticipant_SettlesBillAndRaisesBillSettled()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto", "Caro");
        var pending = bill.Participants.Where(p => p.Status == ParticipantStatus.Pending).ToList();

        foreach (var p in pending)
        {
            bill.RegisterPayment(p.Id, p.Share, Now.AddMinutes(1));
        }

        bill.Status.ShouldBe(BillStatus.Settled);
        bill.SettledAt.ShouldBe(Now.AddMinutes(1));
        bill.DomainEvents.OfType<BillSettled>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RegisterPayment_ParticipantAlreadyPaid_IsUnexpectedDuplicateAndKeepsState()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto", "Caro");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        bill.RegisterPayment(beto.Id, beto.Share, Now);

        var outcome = bill.RegisterPayment(beto.Id, beto.Share, Now.AddMinutes(1));

        outcome.ShouldBe(PaymentOutcome.UnexpectedAlreadyPaid);
        beto.PaidAt.ShouldBe(Now);
        bill.DomainEvents.OfType<UnexpectedPaymentReceived>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("33.00")]
    [InlineData("50.00")]
    public void RegisterPayment_AmountDiffersFromShare_IsUnexpectedAndParticipantStaysPending(string paid)
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto", "Caro");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");

        var outcome = bill.RegisterPayment(beto.Id, Mxn_(paid), Now);

        outcome.ShouldBe(PaymentOutcome.UnexpectedAmountMismatch);
        beto.Status.ShouldBe(ParticipantStatus.Pending);
        bill.DomainEvents.OfType<UnexpectedPaymentReceived>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RegisterPayment_UnknownParticipant_ThrowsBillRule()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");

        Should.Throw<BillRuleException>(() => bill.RegisterPayment(Guid.CreateVersion7(), Mxn_("50"), Now));
    }

    [Fact]
    public void RegisterPayment_OnCancelledBill_IsUnexpectedBillNotOpen()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        bill.Cancel(Now);

        var outcome = bill.RegisterPayment(beto.Id, beto.Share, Now.AddMinutes(1));

        outcome.ShouldBe(PaymentOutcome.UnexpectedBillNotOpen);
        beto.Status.ShouldBe(ParticipantStatus.Pending);
    }

    [Fact]
    public void RegisterPayment_InOtherCurrency_IsUnexpectedAmountMismatch()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        var usd = Money.Of(beto.Share.Amount, new Currency("USD", 2));

        bill.RegisterPayment(beto.Id, usd, Now).ShouldBe(PaymentOutcome.UnexpectedAmountMismatch);
    }

    // ------------------------------------------------------------ progress

    [Fact]
    public void Progress_IsDerivedFromParticipantStatuses()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto", "Caro");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        bill.RegisterPayment(beto.Id, beto.Share, Now);

        // owner 33.34 waived + Beto 33.33 paid = 66.67 covered; Caro 33.33 remaining
        bill.CoveredAmount.ShouldBe(Mxn_("66.67"));
        bill.RemainingAmount.ShouldBe(Mxn_("33.33"));
    }

    // ---------------------------------------------------------- cancelling

    [Fact]
    public void Cancel_OpenBill_SetsCancelledAndRaisesEvent()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");

        bill.Cancel(Now.AddMinutes(1));

        bill.Status.ShouldBe(BillStatus.Cancelled);
        bill.CancelledAt.ShouldBe(Now.AddMinutes(1));
        bill.DomainEvents.OfType<BillCancelled>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Cancel_SettledBill_ThrowsBillState()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        bill.RegisterPayment(beto.Id, beto.Share, Now);

        Should.Throw<BillStateException>(() => bill.Cancel(Now));
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ThrowsBillState()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");
        bill.Cancel(Now);

        Should.Throw<BillStateException>(() => bill.Cancel(Now));
    }

    // -------------------------------------------------------------- events

    [Fact]
    public void ClearDomainEvents_RemovesPendingEvents()
    {
        var bill = CreateEqualBill("100", "Ana (owner)", "Beto");

        bill.ClearDomainEvents();

        bill.DomainEvents.ShouldBeEmpty();
    }
}
