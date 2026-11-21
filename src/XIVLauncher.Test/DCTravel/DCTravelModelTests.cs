using System.Text.Json;
using XIVLauncher.DCTravel;
using Xunit;

namespace XIVLauncher.Test.DCTravel;

/// <summary>
///     跨大区旅行（DCTravel）模型与状态逻辑验证（纯类型/序列化，无网络）
/// </summary>
public sealed class DCTravelModelTests
{
    [Fact]
    public void StatusType_Enum_Values_AreStable()
    {
        // 状态枚举值稳定性（对外 RPC 协议依赖）
        Assert.Equal(-5, (int)DCTravelStatusType.TravelFailed);
        Assert.Equal(-1, (int)DCTravelStatusType.PreCheckFailed);
        Assert.Equal(0,  (int)DCTravelStatusType.Checking);
        Assert.Equal(5,  (int)DCTravelStatusType.Success);
    }

    [Fact]
    public void APIType_Enum_IsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(DCTravelAPIType), DCTravelAPIType.Travel));
        Assert.True(Enum.IsDefined(typeof(DCTravelAPIType), DCTravelAPIType.TravelWithTicket));
        Assert.True(Enum.IsDefined(typeof(DCTravelAPIType), DCTravelAPIType.Order));
    }

    [Fact]
    public void MaintenanceState_Enum_IsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(DCTravelMaintenanceState), DCTravelMaintenanceState.Normal));
        Assert.True(Enum.IsDefined(typeof(DCTravelMaintenanceState), DCTravelMaintenanceState.UnderMaintenance));
    }

    [Fact]
    public void Character_Deserializes_FromJson()
    {
        const string JSON = """{"roleId":"12345","roleName":"光之战士"}""";

        var character = JsonSerializer.Deserialize<DCTravelCharacter>(JSON);

        Assert.NotNull(character);
        Assert.Equal("12345", character!.ContentID);
        Assert.Equal("光之战士", character.Name);
    }

    [Fact]
    public void MigrationOrder_Deserializes_FromJson()
    {
        const string JSON = """{"orderId":"O-1","roleId":"r1","groupId":2,"groupName":"陆行鸟","travelStatus":3}""";

        var order = JsonSerializer.Deserialize<DCTravelMigrationOrder>(JSON);

        Assert.NotNull(order);
        Assert.Equal("O-1", order!.OrderID);
        Assert.Equal("陆行鸟", order.GroupName);
        Assert.Equal(3, order.TravelStatus);
    }

    [Fact]
    public void MigrationOrders_WrapsList()
    {
        var orders = new DCTravelMigrationOrders { Orders = [new DCTravelMigrationOrder { OrderID = "x" }] };

        Assert.Single(orders.Orders);
    }

    [Fact]
    public void APIException_HasMessage()
    {
        var ex = new DCTravelAPIException("测试错误");
        Assert.Equal("测试错误", ex.Message);
        Assert.NotNull(ex.ToString());
    }
}
