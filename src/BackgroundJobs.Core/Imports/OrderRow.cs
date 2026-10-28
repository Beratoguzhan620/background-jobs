namespace BackgroundJobs.Core.Imports;

public sealed record OrderRow(string OrderNo, DateTimeOffset OrderedAt, string CustomerEmail, string Sku, int Quantity, long UnitPriceMinor);
