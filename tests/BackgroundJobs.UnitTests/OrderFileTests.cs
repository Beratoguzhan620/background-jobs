using BackgroundJobs.Core.Imports;

namespace BackgroundJobs.UnitTests;

public class OrderFileTests
{
    private static readonly Dictionary<string, int> Positions = OrderFile.Columns.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);

    [Fact]
    public void Header_columns_may_come_in_any_order_and_case()
    {
        Assert.True(OrderFile.TryReadHeader(["﻿SKU", "Quantity", "unit_price", "order_no", "ordered_at", "customer_email"], out var positions, out _));
        Assert.Equal(0, positions["sku"]);
    }

    [Fact]
    public void A_missing_column_is_named()
    {
        Assert.False(OrderFile.TryReadHeader(["order_no", "sku"], out _, out var problem));
        Assert.Contains("ordered_at", problem);
        Assert.Contains("unit_price", problem);
    }

    [Fact]
    public void A_valid_row_becomes_minor_units_and_turkish_midnight()
    {
        var row = OrderFile.Parse(["A-1", "2026-10-28", " Ayse@Example.com ", "sku-9", "3", "12.5"], Positions, out var reason);

        Assert.Null(reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(3)), row!.OrderedAt);
        Assert.Equal("ayse@example.com", row.CustomerEmail);
        Assert.Equal("SKU-9", row.Sku);
        Assert.Equal(1250, row.UnitPriceMinor);
    }

    [Theory]
    [InlineData("A 1", "2026-10-28", "a@b.co", "S", "1", "1", "order_no")]
    [InlineData("A-1", "28.10.2026", "a@b.co", "S", "1", "1", "ordered_at")]
    [InlineData("A-1", "2026-10-28", "nobody", "S", "1", "1", "customer_email")]
    [InlineData("A-1", "2026-10-28", "a@b.co", "", "1", "1", "sku")]
    [InlineData("A-1", "2026-10-28", "a@b.co", "S", "0", "1", "quantity")]
    [InlineData("A-1", "2026-10-28", "a@b.co", "S", "1.5", "1", "quantity")]
    [InlineData("A-1", "2026-10-28", "a@b.co", "S", "1", "1.234", "unit_price")]
    [InlineData("A-1", "2026-10-28", "a@b.co", "S", "1", "-2", "unit_price")]
    public void Invalid_rows_say_which_field_is_wrong(string orderNo, string date, string email, string sku, string quantity, string price, string field)
    {
        var row = OrderFile.Parse([orderNo, date, email, sku, quantity, price], Positions, out var reason);

        Assert.Null(row);
        Assert.StartsWith(field, reason);
    }
}
