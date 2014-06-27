using System.Web.Mvc;
using System.Web.Routing;
using Keepzer.Trackers.Logic;

namespace Keepzer.Trackers
{
	public class MvcApplication : System.Web.HttpApplication
	{
		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();

			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			RouteConfig.RegisterRoutes(RouteTable.Routes);


			// load consumers
			ServiceManager serviceManager = new ServiceManager();
			serviceManager.FindServices();
		}
	}
}