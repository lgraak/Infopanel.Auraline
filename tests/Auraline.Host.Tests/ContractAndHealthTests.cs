using System.Text.Json;
using Auraline.Contracts;
using Auraline.Host.Web;

namespace Auraline.Host.Tests;

public sealed class ContractAndHealthTests
{
    [Fact]
    public void ContractCompatibilityUsesMajorVersion()
    {
        Assert.True(new ContractVersion(1, 0).IsCompatibleWith(new ContractVersion(1, 9)));
        Assert.False(new ContractVersion(1, 0).IsCompatibleWith(new ContractVersion(2, 0)));
        Assert.Equal("1.0", ContractVersion.Current.ToString());
    }

    [Fact]
    public void HealthContractSerializesStableMachineReadableFields()
    {
        var health = new HealthContract("healthy", "1.0.0-m1", new(1, 1, 0, 1),
            [new("provider-1", "Provider", true, "Reconnecting", 0, "connection refused")], null);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(health));
        var root = document.RootElement;
        Assert.Equal("healthy", root.GetProperty("host_status").GetString());
        Assert.Equal("1.0.0-m1", root.GetProperty("host_version").GetString());
        Assert.Equal(1, root.GetProperty("provider_summary").GetProperty("configured").GetInt32());
        Assert.Equal("Reconnecting", root.GetProperty("providers")[0].GetProperty("state").GetString());
        Assert.Equal("connection refused", root.GetProperty("providers")[0].GetProperty("last_error").GetString());
    }
}
