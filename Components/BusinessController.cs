using DotNetNuke.Entities.Modules;

namespace Dnn.Modules.RazorMvcHost.Components
{
    /// <summary>
    /// Minimal DNN business controller. The module has no IPortable / ISearchable
    /// hooks, but DNN expects the manifest's businessControllerClass to resolve.
    /// </summary>
    public class BusinessController : IUpgradeable
    {
        public string UpgradeModule(string version)
        {
            return "Success";
        }
    }
}
