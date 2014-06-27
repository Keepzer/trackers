using System;
using System.Web.Mvc;
using Keepzer.Trackers.Logic;

namespace Keepzer.Trackers.Controllers
{
	public class ServiceSettings
	{
		
	}


    public class SettingsController : Controller
    {
		public ActionResult Service(Guid id)
		{
			SettingsManager manager = new SettingsManager();
			manager.GetServiceSettings(id);
			return new EmptyResult();
		}

		[AcceptVerbs(HttpVerbs.Post)]
		public ActionResult Service(ServiceSettings settings)
        {
            return new EmptyResult();
        }

    }
}
