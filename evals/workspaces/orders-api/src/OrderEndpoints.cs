namespace Orders.Api;

public static class OrderEndpoints
{
    public static void MapOrders(this WebApplication app)
    {
        app.MapGet("/customers/{id:int}/totals", (int id, OrderService service, CancellationToken cancellationToken) =>
            service.GetTotals(id));
    }
}
