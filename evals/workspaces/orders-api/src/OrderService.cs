namespace Orders.Api;

public sealed class OrderService(OrderRepository repository, PricingClient pricing)
{
    public decimal GetTotals(int customerId)
    {
        var orders = repository.GetOrders(customerId);
        // Appel asynchrone bloqué : le thread attend la réponse HTTP sans rien faire d'autre.
        var rates = pricing.GetRatesAsync().Result;
        return orders.Sum(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice) * rates.Multiplier);
    }
}
