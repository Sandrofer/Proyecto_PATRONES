using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Application;
using Pos.Infrastructure.Odoo;

namespace Pos.UnitTests.Infrastructure;

// Contract tests of the Odoo adapter: they check the JSON-2 requests it sends
// and how it reads Odoo's answers, without a running Odoo.
public class OdooPosGatewayTests
{
    sealed class FakeOdoo(Func<string, JsonNode?, string> respond) : HttpMessageHandler
    {
        public List<(string Path, JsonNode? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            string path = request.RequestUri!.AbsolutePath;
            Calls.Add((path, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(path, body), Encoding.UTF8, "application/json")
            };
        }
    }

    static OdooPosGateway Gateway(HttpMessageHandler handler)
    {
        var options = Options.Create(new OdooOptions { ApiKey = "k", Database = "pos", PosConfigId = 1 });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://odoo.test/") };
        return new OdooPosGateway(new OdooJson2Client(http, options, NullLogger<OdooJson2Client>.Instance), options);
    }

    const string ProductRow = """
        [{"id":3,"name":"Café americano","display_name":"[CAF-01] Café americano","default_code":"CAF-01",
          "lst_price":1.5,"taxes_id":[1],"available_in_pos":true,"sale_ok":true,"is_storable":true,
          "qty_available":100.0,"image_128":{"content":"iVBORw0K","mimetype":"image/png"}}]
        """;
    const string TaxRow = """[{"id":1,"name":"IVA 15%","amount":15.0,"amount_type":"percent","price_include":false}]""";

    [Fact]
    public async Task Search_sends_the_term_in_the_domain_and_reads_products()
    {
        var odoo = new FakeOdoo((path, _) => path.Contains("account.tax") ? TaxRow : ProductRow);

        var products = await Gateway(odoo).SearchProductsAsync("caf", 10, default);

        string domain = odoo.Calls[0].Body!["domain"]!.ToJsonString();
        Assert.Contains("\"ilike\",\"caf\"", domain);
        var p = Assert.Single(products);
        Assert.Equal("Café americano", p.Name);
        Assert.Equal(15m, p.Taxes.TotalPercent);
        Assert.Equal(100m, p.QuantityAvailable);
        Assert.Equal("iVBORw0K", p.ImageBase64);
    }

    [Fact]
    public async Task Odoo_false_values_are_read_as_empty()
    {
        var odoo = new FakeOdoo((path, _) => path.Contains("account.tax") ? "[]" :
            """[{"id":9,"name":"Agua","default_code":false,"lst_price":1,"taxes_id":[],"available_in_pos":true,"sale_ok":true,"is_storable":false,"qty_available":0,"image_128":false}]""");

        var p = Assert.Single(await Gateway(odoo).SearchProductsAsync(null, 10, default));

        Assert.Null(p.Code);
        Assert.Null(p.ImageBase64);
        Assert.Null(p.QuantityAvailable);
    }

    [Fact]
    public async Task A_draft_order_is_not_reported_as_registered()
    {
        var odoo = new FakeOdoo((_, _) => """[{"id":4,"name":"Shop/0004","state":"draft","amount_total":5}]""");

        Assert.Null(await Gateway(odoo).FindOrderByReferenceAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Non_percentage_taxes_are_rejected()
    {
        var odoo = new FakeOdoo((path, _) => path.Contains("account.tax")
            ? """[{"id":1,"name":"Fijo","amount":1,"amount_type":"fixed","price_include":false}]"""
            : ProductRow);

        await Assert.ThrowsAsync<ExternalServiceRejectedException>(() => Gateway(odoo).GetProductsAsync([3], default));
    }
}
