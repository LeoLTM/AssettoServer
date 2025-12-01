using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace BestLapTimesPlugin;

public class BestLapTimesModule : AssettoServerModule<BestLapTimesConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<BestLapTimesPlugin>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
