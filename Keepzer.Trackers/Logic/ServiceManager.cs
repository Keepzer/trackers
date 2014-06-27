using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Policy;
using Keepzer.Data;
using Keepzer.Data.Model;
using log4net;

namespace Keepzer.Trackers.Logic
{
	/// <summary>
	/// Manages connected services
	/// </summary>
	public class ServiceManager
	{
		private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
		private static Dictionary<Guid, ServiceConsumerInfo> serviceConsumers;

		private class ServiceConsumerInfo
		{
			public Guid Id { get; set; }
			public IServiceConsumer ServiceConsumer { get; set; }
		}

		/// <summary>
		/// Helper class which loads consumer types from an assembly
		/// </summary>
		public class LoadServiceTypesProxy : MarshalByRefObject
		{
			public String[] LoadServiceTypes(String assFile, out String assemblyName)
			{
				Assembly asm = Assembly.LoadFrom(assFile);
				assemblyName = asm.FullName;
				try
				{
					IEnumerable<Type> consumerTypes = asm.GetExportedTypes().Where(t => typeof (IServiceConsumer).IsAssignableFrom(t));
					return consumerTypes.Where(t => t != typeof (IServiceConsumer)).Select(t => t.FullName).ToArray();
				}
				catch (TypeLoadException ex)
				{
					Log.Error("LoadServiceTypesProxy::" + ex.Message + "::" + assemblyName);
					return new String[0];
				}
				catch (ReflectionTypeLoadException ex)
				{
					Log.Error("LoadServiceTypesProxy::" + ex.Message + "::" + assemblyName);
					return new String[0];
				}
			}
		}

		private Dictionary<Guid, ServiceConsumerInfo> ServiceConsumers
		{
			get
			{
				// load services if none loaded yet
				if (serviceConsumers == null)
					FindServices();
				return serviceConsumers;
			}
		}


		/// <summary>
		/// Find all available services in all assemblies
		/// </summary>
		public void FindServices()
		{
			serviceConsumers = new Dictionary<Guid, ServiceConsumerInfo>();

			AppDomainSetup setup = new AppDomainSetup();
			setup.ApplicationName = "TempDomain";
			setup.ApplicationBase = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
			setup.ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
			setup.PrivateBinPath = AppDomain.CurrentDomain.SetupInformation.PrivateBinPath;
			setup.PrivateBinPathProbe = AppDomain.CurrentDomain.SetupInformation.PrivateBinPathProbe;
			AppDomain tempDomain = AppDomain.CreateDomain("TempDomain", AppDomain.CurrentDomain.Evidence, setup, AppDomain.CurrentDomain.PermissionSet, new StrongName[0]);
			var proxy = tempDomain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName, typeof(LoadServiceTypesProxy).FullName) as LoadServiceTypesProxy;
			if (proxy == null)
			{
				Log.Error("Proxy is null!");
				return;
			}

			// find all types which implement IServiceConsumer
			String path = (AppDomain.CurrentDomain.SetupInformation.PrivateBinPath ?? AppDomain.CurrentDomain.SetupInformation.ApplicationBase);
			DirectoryInfo binDir = new DirectoryInfo(path);
			Log.Debug("Scanning for service consumers in '" + binDir.FullName + "'.");
			foreach (FileInfo assemblyFile in binDir.GetFiles("*.dll"))
			{
				try
				{
					String assemblyName;
					String[] consumerTypes = proxy.LoadServiceTypes(assemblyFile.FullName, out assemblyName);

					foreach (String consumerType in consumerTypes)
					{
						IServiceConsumer consumer = tempDomain.CreateInstanceAndUnwrap(assemblyName, consumerType) as IServiceConsumer;
						if (consumer == null || String.IsNullOrEmpty(consumer.Name))
						{
							Log.Error("Error instantiating type or consumer of type " + consumerType + " has an invalid name.");
							continue;
						}

						LoadConsumer(assemblyName, consumerType, consumer.Name);
					}
				}
				catch (BadImageFormatException)
				{
					// don't log those errors
				}
				catch (Exception ex)
				{
					Log.Error("ServiceManager.FindServices", ex);
				}
			}
			AppDomain.Unload(tempDomain);

			// create the apps for the services
			// store the service settings in the database
			SettingsManager settingsManager = new SettingsManager();
			List<ServiceConsumerInfo> tempConsumers = new List<ServiceConsumerInfo>(ServiceConsumers.Values);
			foreach (ServiceConsumerInfo consumerInfo in tempConsumers)
			{
				IServiceConsumer consumer = consumerInfo.ServiceConsumer;

				try
				{
					// save default authentication settings if they don't exist
					AuthSettingsBase authSettings = settingsManager.GetServiceSettings(consumerInfo.Id);
					if (authSettings == null || authSettings.GetType().Name != consumer.Settings.GetType().Name)
						settingsManager.SaveServiceSettings(consumerInfo.Id, consumer.Settings);
					else
						consumer.Settings = authSettings;
				}
				catch (Exception ex)
				{
					Log.Error("ServiceManager.FindServices.InitializeConsumer", ex);
					UnloadConsumer(consumerInfo);
				}
			}
		}


		/// <summary>
		/// Load a consumer object into its own app domain
		/// </summary>
		/// <param name="assemblyName"></param>
		/// <param name="typeName"></param>
		/// <param name="name"></param>
		private void LoadConsumer(String assemblyName, String typeName, String name)
		{
			Assembly assembly = Assembly.Load(assemblyName);
			Type consumerType = assembly.GetType(typeName);
			if (consumerType == null)
			{
				Log.Error("Consumer type could not be loaded: '"+ name +"'");
				return;
			}
			IServiceConsumer consumer = Activator.CreateInstance(assembly.GetType(typeName)) as IServiceConsumer;
			if (consumer == null)
			{
				Log.Error("Consumer instance could not be created or does not derive from IServiceConsumer: '" + name + "'");
				return;
			}

			Object[] attributes = consumer.GetType().GetCustomAttributes(typeof (GuidAttribute), true);
			Guid consumerId = (attributes.Length == 0 ? Guid.Empty : new Guid(((GuidAttribute) attributes[0]).Value));
			if (consumerId == Guid.Empty)
			{
				Log.Error("Consumer '"+ typeName +"' has no Guid assigned");
				return;
			}

			ServiceConsumerInfo info = new ServiceConsumerInfo();
			info.Id = consumerId;
			info.ServiceConsumer = consumer;
			ServiceConsumers[consumerId] = info;
		}


		/// <summary>
		/// Unload a consumer
		/// </summary>
		/// <param name="consumerInfo"></param>
		private void UnloadConsumer(ServiceConsumerInfo consumerInfo)
		{
			if (consumerInfo == null) return;

			ServiceConsumers.Remove(consumerInfo.Id);
		}

	
		/// <summary>
		/// Unload all consumers
		/// </summary>
		public void UnloadServices()
		{
			ServiceConsumers.Clear();
		}

		/// <summary>
		/// Get pre-connection information for a service
		/// </summary>
		/// <param name="appId">The id of the service</param>
		/// <returns>Returns connection information like the config datatype</returns>
		public ServiceConnectData GetConnectData(Guid appId)
		{
			// find the service consumer
			ServiceConsumerInfo consumerInfo;
			if (!ServiceConsumers.TryGetValue(appId, out consumerInfo))
				return new ServiceConnectData();

			IServiceConsumer consumer = consumerInfo.ServiceConsumer;
			return new ServiceConnectData(consumer.ConfigData);
		}
/*
		/// <summary>
		/// Check if the service belonging to the app is authorized
		/// </summary>
		/// <param name="app">The user app for the service</param>
		/// <param name="config">Json config data for the service app</param>
		/// <returns>Returns an empty string if the app is no service app or no authorization is necessary, otherwise a url where the user has to authorize access to the service</returns>
		public String AuthorizeService(UserApp app, String config)
		{
			if (app == null || app.AppKind != AppKind.Service) return "";

			// find the service consumer
			ServiceConsumerInfo consumerInfo;
			if (!this.ServiceConsumers.TryGetValue(app.AppId, out consumerInfo))
				return "";

			AppManager appManager = new AppManager();
			DataClient dataClient = new DataClient();
			DataParser dataParser = new DataParser();
			DataManager dataManager = new DataManager();

			IServiceConsumer consumer = consumerInfo.ServiceConsumer;
			ServiceAppState state = (app.AppState as ServiceAppState) ?? new ServiceAppState();

			// create auth manager
			IAuthManager authManager = null;
			OpenAuth1Manager auth1Manager = null;
			OpenAuth2Manager auth2Manager = null;
			Keepzer.Data.Model.AuthSettingsBase serviceSettings = consumer.Settings;
			if (serviceSettings is OpenAuth1SettingsEntry)
			{
				var authSettings = serviceSettings as OpenAuth1SettingsEntry;
				auth1Manager = new OpenAuth1Manager(authSettings, app.AppId, app.UserAppId);
				authManager = new AuthManager(auth1Manager);
			}
			if (serviceSettings is OpenAuth2SettingsEntry)
			{
				var authSettings = serviceSettings as OpenAuth2SettingsEntry;
				auth2Manager = new OpenAuth2Manager(authSettings, app.AppId, app.UserAppId);
				authManager = new AuthManager(auth2Manager);
			}

			// if config data was submitted and we know the datatype then update config data
			if (consumer.ConfigData != null && !String.IsNullOrEmpty(consumer.ConfigData.ConfigDataType) && !String.IsNullOrEmpty(config))
			{
				dataManager.UpdateSingletonData(app.UserAppId, consumer.ConfigData.ConfigDataType, default(LocalDate), config);
			}


			ServiceContext context = new ServiceContext(dataClient, dataParser, authManager, app.UserAppId);
			context.SyncState = new Keepzer.Data.Model.SyncState(state.SyncCurrentObject, state.SyncCurrentDateUtc);
			if (consumer.ValidateAuthorization(context))
			{
				// the service is still authorized so make sure the job exists and just return
				JobManager jobManager = new JobManager();
				JobData jobData = GetSyncJobData(app.UserAppId);
				jobManager.AddJobs(new[] { jobData }, true);

				appManager.SetServiceState(app.UserAppId, serviceState => serviceState.SyncState = SyncState.Success);
				return "";
			}

			// get authorization url
			if (auth1Manager != null)
				return auth1Manager.GetAuthorizationUrl();
			if (auth2Manager != null)
			{
				OpenAuth2SettingsEntry authSettings = serviceSettings as OpenAuth2SettingsEntry;
				return auth2Manager.GetAuthorizationUrl(authSettings.Scopes);
			}

			return "";
		}


		private JobData GetSyncJobData(Guid serviceId)
		{
			JobData jobData = new JobData(typeof(SyncUserServiceJob), TimeHelper.GetUserLocalTime(DateTime.UtcNow.Date), TimeSpan.FromHours(1), 
											UserContext.Current.UserId, new Dictionary<String, Object> { { "serviceId", serviceId } });
			return jobData;
		}

		/// <summary>
		/// Check if the service belonging to the app is authorized
		/// </summary>
		/// <param name="app">The user app for the service</param>
		/// <returns>Returns an empty string if the app is no service app or no authorization is necessary, otherwise a url where the user has to authorize access to the service</returns>
		public Boolean ProcessServiceAuthorization(UserApp app)
		{
			if (app == null || app.AppKind != AppKind.Service) return false;

			DataManager dataManager = new DataManager();

			// find the service consumer
			ServiceConsumerInfo consumerInfo;
			if (!this.ServiceConsumers.TryGetValue(app.AppId, out consumerInfo))
			{
				Log.Error("No service consumer for " + app.AppId);
				return false;
			}

			IServiceConsumer consumer = consumerInfo.ServiceConsumer;
			ServiceAppState state = (app.AppState as ServiceAppState) ?? new ServiceAppState();
			AuthManager authManager = null;

			// find the service configuration
			Keepzer.Data.Model.AuthSettingsBase serviceSettings = consumer.Settings;
			if (serviceSettings is OpenAuth1SettingsEntry)
			{
				OpenAuth1SettingsEntry authSettings = serviceSettings as OpenAuth1SettingsEntry;
				OpenAuth1Manager auth1Manager = new OpenAuth1Manager(authSettings, app.AppId, app.UserAppId);
				authManager = new AuthManager(auth1Manager);
				OpenAuth1 authData = auth1Manager.HandleAuthorizationCallback();

				// store the new access token
				dataManager.UpdateSingletonData(app.UserAppId, PublicDataTypes.OpenAuth1, authData);
			}
			else if (serviceSettings is OpenAuth2SettingsEntry)
			{
				OpenAuth2SettingsEntry authSettings = serviceSettings as OpenAuth2SettingsEntry;
				OpenAuth2Manager auth2Manager = new OpenAuth2Manager(authSettings, app.AppId, app.UserAppId);
				authManager = new AuthManager(auth2Manager);
				OpenAuth2 authData = auth2Manager.HandleAuthorizationCallback();

				// store the new access token
				dataManager.UpdateSingletonData(app.UserAppId, PublicDataTypes.OpenAuth2, authData);
			}

			UserContext.Current.EnsurePassword();
			DataClient dataClient = new DataClient();
			DataParser dataParser = new DataParser();
			ServiceContext context = new ServiceContext(dataClient, dataParser, authManager, app.UserAppId);
			context.SyncState = new Keepzer.Data.Model.SyncState(state.SyncCurrentObject, state.SyncCurrentDateUtc);
			Boolean isAuthorized = consumer.ValidateAuthorization(context);

			// if the service was successfully authorized then add a job to do the synching
			JobManager jobManager = new JobManager();
			JobData jobData = GetSyncJobData(app.UserAppId);
			jobManager.AddJobs(new[] { jobData }, true);

			// update the state
			AppManager appManager = new AppManager();
			appManager.SetServiceState(app.UserAppId, serviceState =>
			{
				serviceState.SyncState = (isAuthorized ? SyncState.Success : SyncState.Unauthorized);
				serviceState.NextSynchronizationUtc = jobData.StartDate;
			});

			return isAuthorized;
		}


		/// <summary>
		/// Sync the service application
		/// </summary>
		/// <param name="serviceId">Id of the service app</param>
		public void SyncService(Guid serviceId)
		{
			AppManager appManager = new AppManager();
			UserApp userApp = appManager.FindUserApp(serviceId);
			if (userApp == null || userApp.AppKind != AppKind.Service)
				throw new ServiceManagerException(ServiceManagerError.InvalidServiceId, serviceId + ":" + (userApp != null ? userApp.AppKind.ToString() : ""));

			ServiceConsumerInfo info;
			if (!this.ServiceConsumers.TryGetValue(userApp.AppId, out info))
				throw new ServiceManagerException(ServiceManagerError.InvalidConsumer, userApp.AppId.ToString());
			IServiceConsumer consumer = info.ServiceConsumer;

			// if off or unauthorized then don't sync
			ServiceAppState state = userApp.AppState as ServiceAppState;
			if (state == null || state.SyncState == SyncState.Off || state.SyncState == SyncState.Unauthorized)
				throw new UnauthorizedAccessException("Service state does not permit sync: " + (state == null ? "<null>" : state.SyncState.ToString()));

			AuthManager authManager = null;

			// find the service configuration
			Keepzer.Data.Model.AuthSettingsBase serviceSettings = consumer.Settings;
			if (serviceSettings is OpenAuth1SettingsEntry)
			{
				OpenAuth1SettingsEntry authSettings = serviceSettings as OpenAuth1SettingsEntry;
				OpenAuth1Manager auth1Manager = new OpenAuth1Manager(authSettings, userApp.AppId, userApp.UserAppId);
				authManager = new AuthManager(auth1Manager);
			}
			else if (serviceSettings is OpenAuth2SettingsEntry)
			{
				OpenAuth2SettingsEntry authSettings = serviceSettings as OpenAuth2SettingsEntry;
				OpenAuth2Manager auth2Manager = new OpenAuth2Manager(authSettings, userApp.AppId, userApp.UserAppId);
				authManager = new AuthManager(auth2Manager);
			}

			UserContext.Current.EnsurePassword();

			// check if authorized before running
			DataClient dataClient = new DataClient();
			DataParser dataParser = new DataParser();
			ServiceContext context = new ServiceContext(dataClient, dataParser, authManager, serviceId);
			context.SyncState = new Keepzer.Data.Model.SyncState(state.SyncCurrentObject, state.SyncCurrentDateUtc);

			if (!info.ServiceConsumer.ValidateAuthorization(context))
			{
				appManager.SetServiceState(serviceId, serviceState => serviceState.SyncState = SyncState.Unauthorized);
				throw new UnauthorizedAccessException("ValidateAuthorization failed");
			}

			Boolean fullSync = false; //(DateTime.UtcNow.Day == 1);
			SyncState syncState = SyncState.Success;

			try
			{
				appManager.SetServiceState(serviceId, serviceState => serviceState.SyncState = (fullSync ? SyncState.SynchronizingFull : SyncState.SynchronizingPartial));
				info.ServiceConsumer.Sync(context, fullSync);
			}
			catch (ClientException ex)
			{
				syncState = (ex.Error == ClientError.Forbidden ? SyncState.Unauthorized : SyncState.Error);
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				syncState = SyncState.Unauthorized;
				throw;
			}
			catch
			{
				syncState = SyncState.Error;
				throw;
			}
			finally
			{
				// log synchronization result
				appManager.SetServiceState(serviceId, serviceState =>
				{
					serviceState.SyncState = syncState;
					serviceState.LastSynchronizationUtc = DateTime.UtcNow;
					serviceState.SyncCurrentObject = context.SyncState.CurrentObject;
					serviceState.SyncCurrentDateUtc = context.SyncState.CurrentDateUtc;
				});
			}
		}*/
	}
}
