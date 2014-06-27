using System;
using System.Collections.Generic;
using Keepzer.Data.Model;

namespace Keepzer.Trackers.Logic
{
	public class SettingsManager
	{
		private static readonly Dictionary<Guid, AuthSettingsBase> SettingsStore = new Dictionary<Guid, AuthSettingsBase>();

		public AuthSettingsBase GetServiceSettings(Guid id)
		{
			AuthSettingsBase settings;
			SettingsStore.TryGetValue(id, out settings);
			return settings;
		}

		public void SaveServiceSettings(Guid id, AuthSettingsBase settings)
		{
			SettingsStore[id] = settings;
		}
	}
}