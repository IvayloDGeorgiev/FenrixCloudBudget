namespace FenrixCloudBudget.Core.Models;

/// <summary>A monetary amount tied to an ISO-4217 currency. Multi-currency support from day one.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public override string ToString() => $"{Amount:0.##} {Currency}";
}

/// <summary>Abstraction for FX conversion (Phase 4+ may plug a live rate source).</summary>
public interface ICurrencyConverter
{
    /// <summary>Convert an amount into the target currency. Returns the input unchanged if rates are unavailable.</summary>
    Task<Money> ConvertAsync(Money amount, string toCurrency, DateOnly? on = null, CancellationToken ct = default);
}
