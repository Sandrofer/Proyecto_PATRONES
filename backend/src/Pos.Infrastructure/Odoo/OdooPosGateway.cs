using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Pos.Application;
using Pos.Application.Ports;
using Pos.Domain.Catalog;
using Pos.Shared;

namespace Pos.Infrastructure.Odoo;

// Adapter: implements the Application port IPosGateway on top of Odoo 20
// models (product.product, account.tax, pos.session, pos.order...).
// All knowledge about Odoo field names and payload shapes lives here.
public sealed class OdooPosGateway(OdooJson2Client odoo, IOptions<OdooOptions> options) : IPosGateway
{
    static readonly string[] RegisteredStates = ["paid", "done", "invoiced"];
    readonly int _configId = options.Value.PosConfigId;

    static readonly string[] ProductFields =
        ["id", "name", "default_code", "lst_price", "taxes_id", "available_in_pos", "sale_ok", "is_storable", "qty_available"];

    public async Task<IReadOnlyList<CatalogProduct>> SearchProductsAsync(string? term, int limit, CancellationToken ct)
    {
        var domain = new JsonArray(Cond("available_in_pos", "=", true), Cond("sale_ok", "=", true));
        if (term is not null)
        {
            domain.Add("|"); domain.Add("|");
            domain.Add(Cond("name", "ilike", term));
            domain.Add(Cond("default_code", "ilike", term));
            domain.Add(Cond("barcode", "=", term));
        }

        var rows = await SearchReadAsync("product.product", domain, [.. ProductFields, "image_128"], ct, limit, "name asc");
        return await ToProductsAsync(rows, ct);
    }

    public async Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        var rows = await SearchReadAsync("product.product",
            new JsonArray(Cond("id", "in", new JsonArray(productIds.Select(id => (JsonNode)id).ToArray()))),
            ProductFields, ct);
        return await ToProductsAsync(rows, ct);
    }

    public async Task<PosSession?> GetActiveSessionAsync(CancellationToken ct)
    {
        var rows = await SearchReadAsync("pos.session",
            new JsonArray(Cond("config_id", "=", _configId), Cond("state", "!=", "closed")),
            ["id", "name", "state", "config_id", "payment_method_ids"], ct, 1, "id desc");
        if (rows.Count == 0)
            return null;

        var s = rows[0]!;
        var methodIds = IdList(s["payment_method_ids"]);
        var methods = methodIds.Count == 0 ? [] : await SearchReadAsync("pos.payment.method",
            new JsonArray(Cond("id", "in", new JsonArray(methodIds.Select(i => (JsonNode)i).ToArray()))),
            ["id", "name", "type"], ct);

        return new PosSession(
            Int(s["id"]), Str(s["name"]) ?? "", Str(s["state"]) ?? "",
            Many2OneId(s["config_id"]), Many2OneName(s["config_id"]) ?? "",
            methods.Select(m => new PaymentMethod(Int(m!["id"]), Str(m["name"]) ?? "", Str(m["type"]) == "cash")).ToList());
    }

    public async Task<TaxRoundingMode> GetTaxRoundingModeAsync(CancellationToken ct)
    {
        var rows = await SearchReadAsync("pos.config", new JsonArray(Cond("id", "=", _configId)), ["company_id"], ct, 1);
        int companyId = rows.Count > 0 ? Many2OneId(rows[0]!["company_id"]) : 0;
        var companies = await SearchReadAsync("res.company", new JsonArray(Cond("id", "=", companyId)),
            ["tax_calculation_rounding_method"], ct, 1);

        return companies.Count > 0 && Str(companies[0]!["tax_calculation_rounding_method"]) == "round_globally"
            ? TaxRoundingMode.Global
            : TaxRoundingMode.PerLine;
    }

    public async Task<RegisteredOrder?> FindOrderByReferenceAsync(Guid reference, CancellationToken ct)
    {
        var rows = await SearchReadAsync("pos.order",
            new JsonArray(Cond("uuid", "=", reference.ToString())),
            ["id", "name", "state", "amount_total"], ct, 1);
        if (rows.Count == 0)
            return null;

        var order = ToRegisteredOrder(rows[0]!);
        // A draft order is not a registered sale: sync_from_ui will update it.
        return RegisteredStates.Contains(order.State) ? order : null;
    }

    public async Task<RegisteredOrder> SubmitPaidOrderAsync(OrderSubmission submission, CancellationToken ct)
    {
        var sale = submission.Sale;
        var totals = submission.Totals;
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var lines = new JsonArray();
        foreach (var amounts in totals.Lines)
        {
            var product = sale.Lines.First(l => l.Product.Id == amounts.ProductId).Product;
            lines.Add(Create(new JsonObject
            {
                ["product_id"] = product.Id,
                ["full_product_name"] = product.Name,
                ["qty"] = amounts.Quantity,
                ["price_unit"] = amounts.UnitPrice,
                ["discount"] = amounts.DiscountPercent,
                ["price_subtotal"] = amounts.SubtotalWithoutTax,
                ["price_subtotal_incl"] = amounts.SubtotalWithTax,
                ["tax_ids"] = new JsonArray(new JsonArray(6, 0,
                    new JsonArray(product.Taxes.Rules.Select(t => (JsonNode)t.Id).ToArray())))
            }));
        }

        var order = new JsonObject
        {
            ["uuid"] = sale.Reference.ToString(),
            ["session_id"] = submission.Session.Id,
            ["state"] = "paid",
            ["date_order"] = now,
            ["amount_tax"] = totals.AmountTax,
            ["amount_total"] = totals.AmountTotal,
            ["amount_paid"] = totals.AmountTotal,
            ["amount_return"] = 0,
            // The cashier (Odoo user) who made the sale.
            ["user_id"] = submission.OperatorUserId is int uid ? uid : null,
            ["lines"] = lines,
            ["payment_ids"] = new JsonArray(Create(new JsonObject
            {
                ["amount"] = totals.AmountTotal,
                ["payment_method_id"] = submission.PaymentMethod.Id,
                ["payment_date"] = now,
                ["name"] = $"Operador: {submission.OperatorName}"
            }))
        };

        await odoo.CallAsync("pos.order", "sync_from_ui", new { orders = new JsonArray(order) }, ct);

        // Read the order back: what we report is what Odoo actually stored (RF-13).
        var stored = await FindAnyOrderAsync(sale.Reference, ct)
            ?? throw new ExternalServiceRejectedException("Odoo respondió, pero la orden no aparece registrada.");
        if (!RegisteredStates.Contains(stored.State))
            throw new ExternalServiceRejectedException($"Odoo guardó la orden {stored.Name} en estado '{stored.State}', no como pagada.");
        return stored;
    }

    public async Task<IReadOnlyList<SaleSummaryDto>> GetRecentOrdersAsync(int limit, CancellationToken ct)
    {
        var rows = await SearchReadAsync("pos.order",
            new JsonArray(Cond("config_id", "=", _configId)),
            ["id", "name", "date_order", "state", "amount_total", "session_id", "user_id"], ct, limit, "date_order desc, id desc");

        return rows.Select(r => new SaleSummaryDto(
            Int(r!["id"]), Str(r["name"]) ?? "", Date(r["date_order"]), Str(r["state"]) ?? "",
            Dec(r["amount_total"]), Many2OneName(r["session_id"]), Many2OneName(r["user_id"]))).ToList();
    }

    public async Task<SaleDetailDto?> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var rows = await SearchReadAsync("pos.order",
            new JsonArray(Cond("id", "=", orderId), Cond("config_id", "=", _configId)),
            ["id", "name", "date_order", "state", "amount_total", "amount_tax", "amount_paid", "session_id", "user_id"], ct, 1);
        if (rows.Count == 0)
            return null;

        var o = rows[0]!;
        var lines = await SearchReadAsync("pos.order.line", new JsonArray(Cond("order_id", "=", orderId)),
            ["full_product_name", "product_id", "qty", "price_unit", "discount", "price_subtotal", "price_subtotal_incl"], ct);
        var payments = await SearchReadAsync("pos.payment", new JsonArray(Cond("pos_order_id", "=", orderId)),
            ["payment_method_id", "amount"], ct);

        return new SaleDetailDto(
            Int(o["id"]), Str(o["name"]) ?? "", Date(o["date_order"]), Str(o["state"]) ?? "",
            Many2OneName(o["session_id"]), Many2OneName(o["user_id"]), Dec(o["amount_tax"]), Dec(o["amount_total"]), Dec(o["amount_paid"]),
            lines.Select(l => new SaleDetailLineDto(
                Str(l!["full_product_name"]) ?? Many2OneName(l["product_id"]) ?? "",
                Dec(l["qty"]), Dec(l["price_unit"]), Dec(l["discount"]),
                Dec(l["price_subtotal"]), Dec(l["price_subtotal_incl"]))).ToList(),
            payments.Select(p => new SalePaymentDto(Many2OneName(p!["payment_method_id"]) ?? "", Dec(p["amount"]))).ToList());
    }

    async Task<RegisteredOrder?> FindAnyOrderAsync(Guid reference, CancellationToken ct)
    {
        var rows = await SearchReadAsync("pos.order", new JsonArray(Cond("uuid", "=", reference.ToString())),
            ["id", "name", "state", "amount_total"], ct, 1);
        return rows.Count == 0 ? null : ToRegisteredOrder(rows[0]!);
    }

    async Task<IReadOnlyList<CatalogProduct>> ToProductsAsync(JsonArray rows, CancellationToken ct)
    {
        var taxIds = rows.SelectMany(r => IdList(r!["taxes_id"])).Distinct().ToList();
        var taxes = new Dictionary<int, TaxRule>();
        if (taxIds.Count > 0)
        {
            var taxRows = await SearchReadAsync("account.tax",
                new JsonArray(Cond("id", "in", new JsonArray(taxIds.Select(i => (JsonNode)i).ToArray()))),
                ["id", "name", "amount", "amount_type", "price_include"], ct);
            foreach (var t in taxRows)
            {
                if (Str(t!["amount_type"]) != "percent")
                    throw new ExternalServiceRejectedException(
                        $"El impuesto '{Str(t["name"])}' no es porcentual; este POS solo admite impuestos porcentuales.");
                taxes[Int(t["id"])] = new TaxRule(Int(t["id"]), Dec(t["amount"]), Bool(t["price_include"]));
            }
        }

        return rows.Select(r => new CatalogProduct(
            Int(r!["id"]),
            Str(r["name"]) ?? "",
            Str(r["default_code"]),
            Dec(r["lst_price"]),
            new TaxSet(IdList(r["taxes_id"]).Where(taxes.ContainsKey).Select(id => taxes[id])),
            Bool(r["available_in_pos"]) && Bool(r["sale_ok"]),
            // Stock is only reliable for storable products (RF-02).
            Bool(r["is_storable"]) ? Dec(r["qty_available"]) : null,
            Image(r["image_128"]))).ToList();
    }

    async Task<JsonArray> SearchReadAsync(string model, JsonArray domain, string[] fields, CancellationToken ct,
        int? limit = null, string? order = null)
    {
        var args = new JsonObject
        {
            ["domain"] = domain,
            ["fields"] = new JsonArray(fields.Select(f => (JsonNode)f).ToArray())
        };
        if (limit is not null) args["limit"] = limit;
        if (order is not null) args["order"] = order;

        return await odoo.CallAsync(model, "search_read", args, ct) as JsonArray ?? [];
    }

    static RegisteredOrder ToRegisteredOrder(JsonNode row) =>
        new(Int(row["id"]), Str(row["name"]) ?? "", Str(row["state"]) ?? "", Dec(row["amount_total"]));

    // Odoo ORM command (0, 0, values) = create a related record.
    static JsonArray Create(JsonObject values) => new(0, 0, values);

    static JsonArray Cond(string field, string op, JsonNode? value) => new(field, op, value);

    // Odoo returns `false` for empty values, so every reader tolerates it.
    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static int Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
    static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    static decimal Dec(JsonNode? n) => n is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : 0m;
    static List<int> IdList(JsonNode? n) => n is JsonArray a ? a.Select(Int).ToList() : [];
    static int Many2OneId(JsonNode? n) => n is JsonArray a && a.Count > 0 ? Int(a[0]) : 0;
    static string? Many2OneName(JsonNode? n) => n is JsonArray a && a.Count > 1 ? Str(a[1]) : null;

    // Odoo 20 returns binary fields as {"content": base64, ...}; older versions as a string.
    static string? Image(JsonNode? n) => n is JsonObject o ? Str(o["content"]) : Str(n);

    static DateTime Date(JsonNode? n) =>
        DateTime.TryParse(Str(n), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d : DateTime.MinValue;
}
