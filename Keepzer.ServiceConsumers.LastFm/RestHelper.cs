using System;

namespace Keepzer.ServiceConsumers.LastFm
{
	internal class RestHelper
	{
		private readonly string apiKey;
		private const String BaseUri = "http://ws.audioscrobbler.com/2.0";

		public RestHelper(String apiKey)
		{
			this.apiKey = apiKey;
		}

		/// <summary>
		/// Get the auth uri
		/// </summary>
		/// <returns></returns>
		public Uri GetAuthUri()
		{
			return new Uri("http://www.last.fm/api/auth/?api_key=" + apiKey, UriKind.Absolute);
		}

		/// <summary>
		/// Get the user profile uri
		/// </summary>
		/// <returns></returns>
		public Uri GetProfileUri()
		{
			return new Uri(BaseUri + "/?method=user.getInfo&apiKey=" + apiKey, UriKind.Absolute);
		}

	}
}
