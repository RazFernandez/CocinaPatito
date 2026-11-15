namespace SplitBill.Domain.Splitting;

/// <summary>How a bill total is distributed among participants. Exactly one method per bill.</summary>
public enum SplitMethod
{
    /// <summary>Total divided by the number of participants with cent adjustment on position 1.</summary>
    Equal = 1,

    /// <summary>Each participant pays an explicit amount; the amounts must add up to the total.</summary>
    Custom = 2,
}
