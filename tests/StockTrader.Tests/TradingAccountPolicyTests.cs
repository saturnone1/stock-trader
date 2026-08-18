using FluentAssertions;
using StockTrader.Application.Accounts;
using StockTrader.Domain.Trading;

namespace StockTrader.Tests;

public sealed class TradingAccountPolicyTests
{
    [Fact]
    public void BrokerIdentifiersRemainStableAndCatalogIsComplete()
    {
        ((int)BrokerType.Alpaca).Should().Be(0);
        ((int)BrokerType.KoreaInvestment).Should().Be(10);
        ((int)BrokerType.Kiwoom).Should().Be(11);
        ((int)BrokerType.LsSecurities).Should().Be(12);
        BrokerCatalog.All.Select(item => item.Type)
            .Should().BeEquivalentTo(Enum.GetValues<BrokerType>());
        BrokerCatalog.All.Should().OnlyContain(item =>
            item.Environments.Contains(item.DefaultEnvironment));
        BrokerCatalog.Get(BrokerType.Alpaca).Capabilities.Should().Be(BrokerCapabilities.Full);
        BrokerCatalog.Get(BrokerType.LsSecurities).Capabilities.Should().Be(BrokerCapabilities.LsSecurities);
        BrokerCatalog.Get(BrokerType.LsSecurities).Capabilities.CanSubmitProtectedEntry.Should().BeFalse();
        BrokerCatalog.Get(BrokerType.KoreaInvestment).Capabilities.Should().Be(BrokerCapabilities.None);
        BrokerCatalog.Get(BrokerType.Kiwoom).Capabilities.Should().Be(BrokerCapabilities.None);
    }

    [Fact]
    public void UnavailableBrokerCanOnlyBeStoredDisabled()
    {
        var unavailable = Account("", "") with
        {
            BrokerType = BrokerType.KoreaInvestment,
            Environment = "Virtual",
            IsEnabled = true,
        };

        TradingAccountPolicy.Validate(unavailable).Errors.Should().ContainSingle(error =>
            error.Contains("not available"));
        TradingAccountPolicy.Validate(unavailable with { IsEnabled = false }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AlpacaRequiresCredentialsAndKnownEnvironment()
    {
        var valid = Account(apiKey: "key", apiSecret: "secret");
        TradingAccountPolicy.Validate(valid).Succeeded.Should().BeTrue();

        var missing = TradingAccountPolicy.Validate(
            valid with { ApiKey = string.Empty, ApiSecret = string.Empty });
        var wrongEnvironment = TradingAccountPolicy.Validate(
            valid with { Environment = "Virtual" });

        missing.Succeeded.Should().BeFalse();
        missing.Errors.Should().ContainSingle(error => error.Contains("required"));
        wrongEnvironment.Errors.Should().ContainSingle(error =>
            error.Contains("Environment"));
    }

    [Fact]
    public void ValidationHandlesNullJsonValuesAndRejectsDisabledActiveAccount()
    {
        var account = Account(apiKey: null!, apiSecret: null!) with
        {
            Environment = null!,
            Notes = null!,
            IsEnabled = false,
            IsActive = true
        };

        var action = () => TradingAccountPolicy.Validate(account);

        action.Should().NotThrow();
        action().Succeeded.Should().BeFalse();
        action().Errors.Should().Contain(error => error.Contains("disabled"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "****")]
    [InlineData("PK123456789", "PK12*******")]
    public void ApiKeyMaskNeverReturnsTheCredential(string key, string expected) =>
        TradingAccountPolicy.MaskApiKey(key).Should().Be(expected);

    private static ManagedTradingAccount Account(string apiKey, string apiSecret) => new()
    {
        AccountName = "Paper",
        BrokerType = BrokerType.Alpaca,
        ApiKey = apiKey,
        ApiSecret = apiSecret,
        Environment = "Paper",
        IsEnabled = true
    };
}
