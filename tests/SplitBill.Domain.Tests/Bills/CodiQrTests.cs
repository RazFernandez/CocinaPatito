using System.Globalization;
using Shouldly;
using SplitBill.Domain.Bills;
using SplitBill.Domain.Bills.Events;
using SplitBill.Domain.Exceptions;
using SplitBill.Domain.Monetary;
using SplitBill.Domain.Payments;

namespace SplitBill.Domain.Tests.Bills;

public sealed class CodiQrTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly PeyNumRef NumRef = PeyNumRef.Parse("0009423");

    private static Bill NewBill()
    {
        var drafts = new List<ParticipantDraft> { new("Ana (owner)", null, true), new("Beto", null, false), new("Caro", null, false) };
        return Bill.CreateEqualSplit(Guid.CreateVersion7(), Guid.CreateVersion7(), "Dinner",
            Money.Of(100m, Currency.Mxn), drafts, Now);
    }

    [Fact]
    public void Lifetime_IsTwentyMinutes()
    {
        CodiQr.Lifetime.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void Issue_ForPendingParticipant_SetsAmountAndExpiryAndRaisesEvent()
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");

        var qr = bill.IssueCodiQr(beto.Id, peyCodiQrId: "35a13c183a", qrString: "{\"TYP\":20}", NumRef, Now);

        qr.ParticipantId.ShouldBe(beto.Id);
        qr.AmountRequested.ShouldBe(beto.Share);
        qr.PeyCodiQrId.ShouldBe("35a13c183a");
        qr.QrString.ShouldBe("{\"TYP\":20}");
        qr.CreatedAt.ShouldBe(Now);
        qr.ExpiresAt.ShouldBe(Now.AddMinutes(20));
        var evt = bill.DomainEvents.OfType<CodiQrIssued>().ShouldHaveSingleItem();
        evt.CodiQrId.ShouldBe(qr.Id);
        evt.ExpiresAt.ShouldBe(qr.ExpiresAt);
    }

    [Fact]
    public void Issue_ForPaidParticipant_ThrowsBillState()
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        bill.RegisterPayment(beto.Id, beto.Share, Now);

        Should.Throw<BillStateException>(() => bill.IssueCodiQr(beto.Id, "x", "y", NumRef, Now));
    }

    [Fact]
    public void Issue_ForOwnerParticipant_ThrowsBillState()
    {
        var bill = NewBill();
        var owner = bill.Participants.Single(p => p.IsOwner);

        Should.Throw<BillStateException>(() => bill.IssueCodiQr(owner.Id, "x", "y", NumRef, Now));
    }

    [Fact]
    public void Issue_OnCancelledBill_ThrowsBillState()
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        bill.Cancel(Now);

        Should.Throw<BillStateException>(() => bill.IssueCodiQr(beto.Id, "x", "y", NumRef, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Issue_BlankPeyIdentifierOrQrString_ThrowsBillRule(string blank)
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");

        Should.Throw<BillRuleException>(() => bill.IssueCodiQr(beto.Id, blank, "qr", NumRef, Now));
        Should.Throw<BillRuleException>(() => bill.IssueCodiQr(beto.Id, "idc", blank, NumRef, Now));
    }

    [Fact]
    public void IsExpired_BeforeExpiry_IsFalse()
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        var qr = bill.IssueCodiQr(beto.Id, "idc", "qr", NumRef, Now);

        qr.IsExpired(Now.AddMinutes(19).AddSeconds(59)).ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_AtOrAfterExpiry_IsTrue()
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        var qr = bill.IssueCodiQr(beto.Id, "idc", "qr", NumRef, Now);

        qr.IsExpired(Now.AddMinutes(20)).ShouldBeTrue();
        qr.IsExpired(Now.AddHours(1)).ShouldBeTrue();
    }

    [Fact]
    public void RemainingLifetime_IsClampedToZeroAfterExpiry()
    {
        var bill = NewBill();
        var beto = bill.Participants.Single(p => p.DisplayName == "Beto");
        var qr = bill.IssueCodiQr(beto.Id, "idc", "qr", NumRef, Now);

        qr.RemainingLifetime(Now.AddMinutes(5)).ShouldBe(TimeSpan.FromMinutes(15));
        qr.RemainingLifetime(Now.AddMinutes(25)).ShouldBe(TimeSpan.Zero);
    }
}

public sealed class PeyNumRefTests
{
    [Theory]
    [InlineData("0000001")]
    [InlineData("9423")]
    [InlineData("1")]
    public void Parse_UpToSevenDigits_IsLeftPaddedToSevenDigits(string raw)
    {
        var numRef = PeyNumRef.Parse(raw);

        numRef.Value.Length.ShouldBe(7);
        numRef.Value.ShouldBe(raw.PadLeft(7, '0'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678")]
    [InlineData("12a4")]
    [InlineData("-123")]
    public void Parse_InvalidReference_ThrowsBillRule(string raw)
    {
        Should.Throw<BillRuleException>(() => PeyNumRef.Parse(raw));
    }

    [Fact]
    public void FromSequence_UsesLastSevenDigits()
    {
        PeyNumRef.FromSequence(123456789).Value.ShouldBe("3456789");
        PeyNumRef.FromSequence(42).Value.ShouldBe("0000042");
    }

    [Fact]
    public void FromSequence_NegativeValue_ThrowsBillRule()
    {
        Should.Throw<BillRuleException>(() => PeyNumRef.FromSequence(-1));
    }
}
