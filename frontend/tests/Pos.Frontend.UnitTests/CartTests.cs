using Pos.Frontend.Domain;

namespace Pos.Frontend.UnitTests;

// P07: cart, quantities and provisional total.
public class CartTests
{
    static readonly CartProduct Coffee = new(10, "Café", "CAF", 2.50m, 15m, false);
    static readonly CartProduct Juice = new(11, "Jugo", "JUG", 4.00m, 15m, false);

    [Fact]
    public void P07_Adding_products_updates_lines_and_provisional_total()
    {
        var cart = new Cart();
        cart.Add(Coffee, 2);
        cart.Add(Juice);

        var totals = cart.CalculateTotals();
        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(9.00m, totals.Untaxed);
        Assert.Equal(1.35m, totals.Tax);
        Assert.Equal(10.35m, totals.Total);
    }

    [Fact]
    public void P07_Changing_quantity_and_removing_recalculate_the_total()
    {
        var cart = new Cart();
        cart.Add(Coffee);
        cart.Add(Juice);

        cart.SetQuantity(Coffee.Id, 4);
        cart.Remove(Juice.Id);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(4, line.Quantity);
        Assert.Equal(11.50m, cart.CalculateTotals().Total);
    }

    [Fact]
    public void P07_Adding_the_same_product_accumulates_quantity()
    {
        var cart = new Cart();
        cart.Add(Coffee);
        cart.Add(Coffee);

        Assert.Equal(2, Assert.Single(cart.Lines).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void P07_Invalid_quantities_are_rejected(decimal quantity)
    {
        var cart = new Cart();
        cart.Add(Coffee);

        Assert.Throws<ArgumentOutOfRangeException>(() => cart.SetQuantity(Coffee.Id, quantity));
        Assert.Equal(1, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Changed_event_notifies_the_view()
    {
        var cart = new Cart();
        int notifications = 0;
        cart.Changed += () => notifications++;

        cart.Add(Coffee);
        cart.SetQuantity(Coffee.Id, 3);
        cart.Remove(Coffee.Id);

        Assert.Equal(3, notifications);
    }
}
