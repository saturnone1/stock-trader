using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Accounts;

namespace StockTrader.Tests;

public sealed class TradingAccountContractTests
{
    [Fact]
    public void AccountResponseMasksKeyAndHasNoSecretField()
    {
        var account = new ManagedTradingAccount
        {
            Id = 4,
            AccountName = "Paper",
            ApiKey = "PK123456789",
            ApiSecret = "never-return-this",
            Environment = "Paper"
        };

        var response = TradingAccountResponse.Create(account);

        response.ApiKey.Should().Be("PK12*******");
        typeof(TradingAccountResponse).GetProperties()
            .Should().NotContain(property => property.Name == "ApiSecret");
        response.ToString().Should().NotContain(account.ApiSecret);
    }

    [Fact]
    public void BlankUpdateCredentialsPreserveExistingValues()
    {
        var existing = new ManagedTradingAccount
        {
            Id = 4,
            AccountName = "Old",
            ApiKey = "key",
            ApiSecret = "secret"
        };
        var request = new TradingAccountUpdateRequest
        {
            AccountName = "New",
            ApiKey = " ",
            ApiSecret = null
        };

        var updated = request.ApplyTo(existing);

        updated.AccountName.Should().Be("New");
        updated.ApiKey.Should().Be("key");
        updated.ApiSecret.Should().Be("secret");
    }
}
