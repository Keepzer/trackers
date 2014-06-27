using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Keepzer.Data;
using Keepzer.Data.DataTypes;
using Keepzer.Data.Exceptions;
using Keepzer.Data.Filter;
using Keepzer.Data.Model;
using Keepzer.Data.Utils;
using Keepzer.ServiceConsumers.LastFm.DataTypes;

namespace Keepzer.ServiceConsumers.LastFm
{
	/// <summary>
	/// Connects the lastfm service to Keepzer
	/// </summary>
	[Guid("F84314C6-151E-408D-B4A6-F265A1782005")]
	public class LastFmServiceConsumer : MarshalByRefObject, IServiceConsumer
	{
		private OpenAuth2SettingsEntry settings = GetDefaultSettings();

		private static readonly ImageHelper.ImageInfo[] Icons =
		{
			new ImageHelper.ImageInfo("Keepzer.ServiceConsumers.LastFm.Images.lastfm-64.png", 64, 64),
			new ImageHelper.ImageInfo("Keepzer.ServiceConsumers.LastFm.Images.lastfm-128.png", 128, 128)
		};

		private static readonly DataTypeReferenceCollection TypeReferences = new DataTypeReferenceCollection
		{
			new DataTypeReference(LastFmDataTypes.UserProfile),
		};

		/// <summary>
		/// The name of the service
		/// </summary>
		public String Name
		{
			get { return "LastFM"; }
		}

		/// <summary>
		/// User friendly title
		/// </summary>
		public String Title
		{
			get { return "LastFM"; }
		}

		/// <summary>
		/// User friendly description
		/// </summary>
		public String Description
		{
			get { return "See your LastFM data in Keepzer"; }
		}

		/// <summary>
		/// List of synchronized datatypes
		/// </summary>
		public DataTypeReferenceCollection DataTypeReferences
		{
			get { return TypeReferences; }
		}

		/// <summary>
		/// Configuration data for the service
		/// </summary>
		public ServiceConnectData ConfigData
		{
			get { return new ServiceConnectData(); }
		}

		/// <summary>
		/// Fetches a default settings object
		/// </summary>
		/// <returns></returns>
		private static OpenAuth2SettingsEntry GetDefaultSettings()
		{
			return new OpenAuth2SettingsEntry
			       {
				       AuthorizationEndpoint = "https://api.moves-app.com/oauth/v1/authorize",
				       TokenEndpoint = "https://api.moves-app.com/oauth/v1/access_token",
					   ClientId = "93f05b8732f098e3f1ad06933491f4e4",
					   ClientSecret = "7aed2f178c5b9a9274fd85afe621190e"
			       };
		}

		/// <summary>
		/// Get the image for this service closest to the given size
		/// </summary>
		/// <param name="width">The requested width of the image</param>
		/// <param name="height">The requested height of the image</param>
		/// <returns></returns>
		public ImageData GetServiceImage(Int32 width, Int32 height)
		{
			ImageData result = new ImageData();
			result.MimeType = "image/png";

			String imageName = ImageHelper.SelectClosestImage(width, height, Icons);

			using (Stream fileStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(imageName))
			{
				if (fileStream == null)
					return result;
				result.Data = new Byte[fileStream.Length];
				fileStream.Read(result.Data, 0, result.Data.Length);
			}

			return result;
		}

		/// <summary>
		/// Get a settings object initilized with default values or set a new one
		/// </summary>
		public AuthSettingsBase Settings
		{
			get { return settings; }
			set { settings = (value as OpenAuth2SettingsEntry) ?? GetDefaultSettings(); }
		}


		/// <summary>
		/// Check whether the service is authorized for the current user
		/// </summary>
		/// <returns></returns>
		public Boolean ValidateAuthorization(ServiceContext context)
		{
			OpenAuth2 authObject = context.DataClient.GetSingletonData(context.UserAppId, PublicDataTypes.OpenAuth2, new OpenAuth2());

			// not authorized if the access token is empty
			if (String.IsNullOrEmpty(authObject.AccessToken))
				return false;

			try
			{
				// validate the authorization token
				GetProfileData(context);
			}
			catch
			{
				return false;
			}
			return true;
		}


		/// <summary>
		/// Syncronize data between keepzer and the service
		/// </summary>
		/// <param name="context"></param>
		/// <param name="fullSync">True if a full sync should be run</param>
		/// <exception cref="Keepzer.Data.Exceptions.ClientException">Thrown when no access token is available</exception>
		public void Sync(ServiceContext context, Boolean fullSync)
		{
			OpenAuth2 authObject = context.DataClient.GetSingletonData(context.UserAppId, PublicDataTypes.OpenAuth2, new OpenAuth2());

			// not authorized if the access token is empty
			if (String.IsNullOrEmpty(authObject.AccessToken))
				throw new ClientException(ClientError.Forbidden);

			LastFmUserProfile profileData = GetProfileData(context);

			// figure out whether to do a full sync or a partial sync
			// do full sync on first of each month
			/*String[] categories = 
			{
				PublicDataTypes.HealthBodySteps, 
				PublicDataTypes.HealthBodyCalories,
				PublicDataTypes.MovementWalking,
				PublicDataTypes.MovementRunning,
				PublicDataTypes.MovementCycling,
				PublicDataTypes.MovementDriving
			};
			DateTime startDate = GetLastEntryDate(context, categories) ?? profileData.FirstDate;
			if (fullSync || startDate < profileData.FirstDate)
				startDate = profileData.FirstDate;
			GetDailySummaryData(context, startDate);*/
		}

		/// <summary>
		/// Get the date of the newest entry
		/// </summary>
		/// <returns></returns>
		private Nullable<DateTime> GetLastEntryDate(ServiceContext context, String[] dataTypes)
		{
			DataFilter search = new DataFilter();
			search.Categories = dataTypes.Select(d => new Category { AppId = context.UserAppId, DataType = d }).ToArray();
			FilterResult searchResult = context.DataClient.Filter(search, 0, 1);
			if (searchResult.Data != null && searchResult.Data.Length > 0)
				return searchResult.Data[0].CreationDate.Utc.Date;
			return null;
		}


		private void AuthorizeRequest(ServiceContext context, HttpWebRequest request)
		{
			OpenAuth2 authObject = context.DataClient.GetSingletonData(context.UserAppId, PublicDataTypes.OpenAuth2, new OpenAuth2());
			context.AuthManager.AuthorizeRequest(request, authObject);
		}

		/// <summary>
		/// Get the user profile
		/// </summary>
		private LastFmUserProfile GetProfileData(ServiceContext context)
		{
			RestHelper rest = new RestHelper("");

			HttpWebRequest request = HttpWebRequest.Create(rest.GetProfileUri()) as HttpWebRequest;
			AuthorizeRequest(context, request);

			WebResponse response = request.GetResponse();
			Stream stream = response.GetResponseStream();
			using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
			{
				// deserialize and save the user profile data
				LastFmUserProfile userProfile = LastFmUserProfile.Deserialize(streamReader);
				context.DataClient.UpdateSingletonData(context.UserAppId, LastFmDataTypes.UserProfile, new LocalDate(userProfile.RegisteredDate), userProfile);
				return userProfile;
			}
		}
	}
}
