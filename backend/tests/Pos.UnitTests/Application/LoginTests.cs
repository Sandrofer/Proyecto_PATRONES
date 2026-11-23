using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pos.Application;
using Pos.Application.Auth;
using Pos.Application.Ports;
using Pos.Infrastructure.Odoo;
using Pos.Shared;

namespace Pos.UnitTests.Application;

public class LoginTests
{
    [Fact]
    public async Task Wrong_credentials_are_rejected()
    {
        var directory = Substitute.For<IOperatorDirectory>();
        directory.ValidateCredentialsAsync("cajero1", "mal", Arg.Any<CancellationToken>()).Returns((Operator?)null);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            new LoginInteractor(directory, Substitute.For<ITokenIssuer>()).HandleAsync(new LoginRequest("cajero1", "mal"), default));
    }

    [Fact]
    public async Task Empty_credentials_do_not_reach_Odoo()
    {
        var directory = Substitute.For<IOperatorDirectory>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new LoginInteractor(directory, Substitute.For<ITokenIssuer>()).HandleAsync(new LoginRequest(" ", ""), default));
        await directory.DidNotReceiveWithAnyArgs().ValidateCredentialsAsync(default!, default!, default);
    }

    [Fact]
    public async Task Valid_cashier_receives_a_token()
    {
        var op = new Operator(8, "cajero1", "Cajero 1");
        var directory = Substitute.For<IOperatorDirectory>();
        directory.ValidateCredentialsAsync("cajero1", "ok", Arg.Any<CancellationToken>()).Returns(op);
        var tokens = Substitute.For<ITokenIssuer>();
        tokens.Issue(op).Returns(new LoginResponse("jwt", DateTime.UtcNow.AddHours(8), "Cajero 1"));

        var response = await new LoginInteractor(directory, tokens).HandleAsync(new LoginRequest(" cajero1 ", "ok"), default);

        Assert.Equal("jwt", response.AccessToken);
    }
}

// Contract tests of the Odoo cashier directory with a simulated Odoo.
public class OdooOperatorDirectoryTests
{
    sealed class FakeOdoo(Func<string, string> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(path), Encoding.UTF8, "application/json")
            });
        }
    }

    static OdooOperatorDirectory Directory(HttpMessageHandler handler)
    {
        var options = Options.Create(new OdooOptions { ApiKey = "k", Database = "pos", PosConfigId = 1 });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://odoo.test/") };
        return new OdooOperatorDirectory(new OdooJson2Client(http, options, NullLogger<OdooJson2Client>.Instance));
    }

    const string AuthOk = """{"jsonrpc":"2.0","result":{"uid":8}}""";
    const string AuthDenied = """{"jsonrpc":"2.0","error":{"code":0,"data":{"name":"odoo.exceptions.AccessDenied"}}}""";

    [Fact]
    public async Task Odoo_access_denied_means_wrong_credentials()
    {
        var odoo = new FakeOdoo(_ => AuthDenied);

        Assert.Null(await Directory(odoo).ValidateCredentialsAsync("cajero1", "mal", default));
        Assert.Single(odoo.Paths);
    }

    [Fact]
    public async Task A_user_without_Point_of_Sale_rights_is_not_authorized()
    {
        var odoo = new FakeOdoo(path => path.Contains("authenticate") ? AuthOk : "false");

        await Assert.ThrowsAsync<OperatorNotAuthorizedException>(() =>
            Directory(odoo).ValidateCredentialsAsync("contador", "ok", default));
    }

    [Fact]
    public async Task A_cashier_with_Point_of_Sale_rights_is_accepted()
    {
        var odoo = new FakeOdoo(path =>
            path.Contains("authenticate") ? AuthOk :
            path.Contains("has_group") ? "true" :
            """[{"id":8,"name":"Cajero 1","login":"cajero1"}]""");

        var op = await Directory(odoo).ValidateCredentialsAsync("cajero1", "ok", default);

        Assert.Equal(new Operator(8, "cajero1", "Cajero 1"), op);
    }
}
