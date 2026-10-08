namespace TaskBoard.Api.IntegrationTests;

public sealed class ZzProtectionProbeTests
{
    [Fact]
    public void Deliberately_fails_to_verify_protection()
        => Assert.True(false, "protection probe");
}
