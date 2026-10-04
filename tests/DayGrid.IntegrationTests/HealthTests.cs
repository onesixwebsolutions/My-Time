using System.Net;
using DayGrid.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace DayGrid.IntegrationTests;

public class HealthTests : IntegrationTestBase
{
    public HealthTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Liveness_And_Readiness_AreHealthy_AgainstRealPostgres()
    {
        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync("/health"));
        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync("/health/ready"));
    }

    [Fact]
    public async Task TestServer_IsPostgres16_OnAnEphemeralPort()
    {
        Assert.NotEqual(5432, Fx.Port);
        await using var connection = new NpgsqlConnection(Fx.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("16.", connection.PostgreSqlVersion.ToString());
    }
}
