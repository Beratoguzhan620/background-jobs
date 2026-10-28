namespace BackgroundJobs.Core.Imports;

/// <summary>An order line brought in by an import; the order number is unique across all imports.</summary>
public sealed class Order
{
    private Order()
    {
        OrderNo = string.Empty;
        CustomerEmail = string.Empty;
        Sku = string.Empty;
    }

    public long Id { get; private set; }

    public string OrderNo { get; private set; }

    public DateTimeOffset OrderedAt { get; private set; }

    public string CustomerEmail { get; private set; }

    public string Sku { get; private set; }

    public int Quantity { get; private set; }

    public long UnitPriceMinor { get; private set; }

    public Guid ImportFileId { get; private set; }
}
