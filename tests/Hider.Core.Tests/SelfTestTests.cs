namespace Hider.Core.Tests
{
    public class SelfTestTests
    {
        [Fact]
        public void CT93_SelfTest_Passes()
        {
            using var output = new StringWriter();
            int code = SelfTest.Run(output);
            Assert.True(code == 0, output.ToString());
        }
    }
}
