using Uno.UI.Hosting;
namespace GridSpace.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => UnoPlatformHostBuilder.Create().App(() => new App()).UseX11().UseLinuxFrameBuffer().UseMacOS().UseWin32().Build().Run();
}
