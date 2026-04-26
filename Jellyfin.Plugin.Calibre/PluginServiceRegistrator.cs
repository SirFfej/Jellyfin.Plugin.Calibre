using Jellyfin.Plugin.Calibre.Logging;
using Jellyfin.Plugin.Calibre.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Calibre;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ILoggerProvider>(sp =>
        {
            var paths = sp.GetRequiredService<IApplicationPaths>();
            return new CalibreFileLoggerProvider(paths.LogDirectoryPath);
        });

        serviceCollection.AddSingleton<LinkCalibreTask>();
        serviceCollection.AddSingleton<EnrichMetadataTask>();
        serviceCollection.AddSingleton<ValidateCalibreTask>();
        serviceCollection.AddSingleton<DuplicationReportTask>();
    }
}