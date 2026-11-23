using Pos.Application.Discounts;
using Pos.Application.Ports;
using Pos.Domain;
using Pos.Domain.Customers;
using Pos.Domain.Sales;
using Pos.Shared;

namespace Pos.Application.Sales;

// Builds a Sale aggregate from a request using prices and availability read
// from Odoo. Shared by the quote and the registration use cases so both
// apply exactly the same rules.
public sealed class SaleBuilder(IPosGateway gateway, IDiscountPolicyCatalog discountPolicies)
{
    public async Task<Sale> BuildAsync(Guid reference, SaleRequest request, CancellationToken ct)
    {
        ValidateShape(request);

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await gateway.GetProductsAsync(productIds, ct)).ToDictionary(p => p.Id);
        var taxRounding = await gateway.GetTaxRoundingModeAsync(ct);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw ValidationException.For("lines",
                $"Los productos {string.Join(", ", missing)} no existen o no están disponibles en Odoo.");

        try
        {
            var sale = new Sale(reference, taxRounding);
            sale.AssignCustomer(BuildCustomer(request.Customer));
            foreach (var line in request.Lines)
                sale.AddProduct(products[line.ProductId], line.Quantity);

            var policy = discountPolicies.Find(request.DiscountPolicyCode)
                ?? throw ValidationException.For("discountPolicyCode",
                    $"La política de descuento '{request.DiscountPolicyCode}' no está habilitada.");
            sale.ApplyDiscount(policy, request.DiscountPercent);

            return sale;
        }
        catch (DomainException ex)
        {
            throw ValidationException.For("sale", ex.Message);
        }
    }

    static Customer? BuildCustomer(SaleCustomerRequest? customer)
    {
        if (customer is null)
            return null;
        try
        {
            return Customer.Create(customer.Identification, customer.Name);
        }
        catch (DomainException ex)
        {
            throw ValidationException.For("customer", ex.Message);
        }
    }

    // RN-01 and RN-02 checked on the raw input before calling Odoo.
    static void ValidateShape(SaleRequest? request)
    {
        if (request?.Lines is null || request.Lines.Count == 0)
            throw ValidationException.For("lines", "La venta debe contener al menos un producto.");

        var errors = new Dictionary<string, string[]>();
        for (int i = 0; i < request.Lines.Count; i++)
        {
            decimal qty = request.Lines[i].Quantity;
            if (qty <= 0)
                errors[$"lines[{i}].quantity"] = ["La cantidad debe ser mayor que cero."];
            else if (qty != decimal.Truncate(qty))
                errors[$"lines[{i}].quantity"] = ["La cantidad debe ser un número entero."];
            if (request.Lines[i].ProductId <= 0)
                errors[$"lines[{i}].productId"] = ["El producto no es válido."];
        }
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
