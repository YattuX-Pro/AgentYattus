namespace Orders.Api;

public sealed class OrderRepository(OrdersDb db)
{
    public List<Order> GetOrders(int customerId)
    {
        var orders = db.Orders.Where(o => o.CustomerId == customerId).ToList();
        foreach (var order in orders)
        {
            // Une requête par commande pour charger ses lignes.
            order.Lines = db.OrderLines.Where(l => l.OrderId == order.Id).ToList();
        }

        return orders;
    }
}
