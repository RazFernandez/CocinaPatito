namespace SplitBill.Domain.Bills;

/// <summary>
/// Input describing one participant when a bill is created. Order in the list defines
/// <c>position</c>; the first entry absorbs the rounding adjustment of an EQUAL split.
/// </summary>
/// <param name="DisplayName">Name shown to the other participants.</param>
/// <param name="UserId">Registered user, or <see langword="null"/> for a guest.</param>
/// <param name="IsOwner">True for the person who already paid the bill; their share is waived.</param>
public sealed record ParticipantDraft(string DisplayName, Guid? UserId, bool IsOwner);
