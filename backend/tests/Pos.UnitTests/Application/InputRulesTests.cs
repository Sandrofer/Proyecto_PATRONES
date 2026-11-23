using NSubstitute;
using Pos.Application;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Ports;
using Pos.Domain.Discounts;
using Pos.Shared;

namespace Pos.UnitTests.Application;

// RNF-09: validation of inputs (special characters, lengths, decimals).
public class InputRulesTests
{
    [Theory]
    [InlineData("café")]
    [InlineData("CAF-01")]
    [InlineData("Sánduche de pollo")]
    [InlineData("jugo 1/2")]
    [InlineData("")]
    public void Normal_searches_are_accepted(string text) => Assert.Null(InputRules.ValidateSearch(text));

    [Theory]
    [InlineData("!\"#!\"#*")]
    [InlineData("café'; DROP")]
    [InlineData("<script>")]
    [InlineData("%")]
    public void Searches_with_special_characters_are_rejected(string text) =>
        Assert.Equal(InputRules.SearchCharactersMessage, InputRules.ValidateSearch(text));

    [Fact]
    public void Too_long_searches_are_rejected() =>
        Assert.NotNull(InputRules.ValidateSearch(new string('a', InputRules.MaxSearchLength + 1)));

    [Theory]
    [InlineData("cajero1", true)]
    [InlineData("ana.torres@uta.edu.ec", true)]
    [InlineData("caje ro", false)]
    [InlineData("!\"#*", false)]
    public void Usernames_are_checked(string text, bool valid) =>
        Assert.Equal(valid, InputRules.ValidateUsername(text) is null);

    [Fact]
    public async Task Api_search_rejects_special_characters_without_calling_Odoo()
    {
        var gateway = Substitute.For<IPosGateway>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new SearchProductsInteractor(gateway).HandleAsync("!\"#*", default));
        await gateway.DidNotReceiveWithAnyArgs().SearchProductsAsync(default, default, default);
    }

    [Fact]
    public async Task Api_login_rejects_special_characters_without_calling_Odoo()
    {
        var directory = Substitute.For<IOperatorDirectory>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            new LoginInteractor(directory, Substitute.For<ITokenIssuer>()).HandleAsync(new LoginRequest("!\"#*", "x"), default));
        Assert.True(ex.Errors.ContainsKey("username"));
        await directory.DidNotReceiveWithAnyArgs().ValidateCredentialsAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(5.555)]
    [InlineData(0.001)]
    public void Manual_discount_rejects_more_than_two_decimals(decimal percent)
    {
        var decision = new ManualPercentageDiscountPolicy(10m).Evaluate(new DiscountContext(50m, percent));
        Assert.False(decision.IsApproved);
        Assert.Contains("2 decimales", decision.RejectionReason);
    }

    [Fact]
    public void Manual_discount_accepts_two_decimals() =>
        Assert.True(new ManualPercentageDiscountPolicy(10m).Evaluate(new DiscountContext(50m, 7.25m)).IsApproved);
}
