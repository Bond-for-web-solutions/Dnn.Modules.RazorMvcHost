using DotNetNuke.Web.Mvc.Routing;

namespace Dnn.Modules.RazorMvcHost
{
    public class RouteConfig : IMvcRouteMapper
    {
        public void RegisterRoutes(IMapRoute mapRouteManager)
        {
            mapRouteManager.MapRoute(
                "Dnn.Modules.RazorMvcHost",
                "Dnn.Modules.RazorMvcHost",
                "{controller}/{action}",
                new[] { "Dnn.Modules.RazorMvcHost.Controllers" });
        }
    }
}
