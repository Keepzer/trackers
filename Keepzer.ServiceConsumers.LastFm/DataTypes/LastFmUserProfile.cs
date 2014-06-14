using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace Keepzer.ServiceConsumers.LastFm.DataTypes
{
	/// <summary>
	/// Moves data for <see cref="LastFmDataTypes.UserProfile"/>
	/// </summary>
	public class LastFmUserProfile
	{
		/*
		<user>
			<id>1000002</id>
			<name>RJ</name>
			<realname>Richard Jones </realname>
			<url>http://www.last.fm/user/RJ</url>
			<image>http://userserve-ak.last.fm/serve/126/8270359.jpg</image>
			<country>UK</country>
			<age>27</age>
			<gender>m</gender>
			<subscriber>1</subscriber>
			<playcount>54189</playcount>
			<playlists>4</playlists>
			<bootstrap>0</bootstrap>
			<registered unixtime="1037793040">2002-11-20 11:50</registered>
		</user>
		*/

		[XmlElement("id")]
		public Int32 Id { get; set; }
		[XmlElement("name")]
		public String Name { get; set; }
		[XmlElement("realname")]
		public String RealName { get; set; }
		[XmlElement("url")]
		public String Url { get; set; }
		[XmlElement("image")]
		public String Image { get; set; }
		[XmlElement("country")]
		public String Country { get; set; }
		[XmlElement("age")]
		public Int32 Age { get; set; }
		[XmlElement("gender")]
		public String Gender { get; set; }
		[XmlElement("subscriber")]
		public Int32 Subscriber { get; set; }
		[XmlElement("playcount")]
		public Int32 PlayCount { get; set; }
		[XmlElement("playlists")]
		public Int32 Playlists { get; set; }
		[XmlElement("bootstrap")]
		public Int32 Bootstrap { get; set; }
		[XmlElement("registered")]
		public String Registered { get; set; }

		[XmlIgnore]
		public DateTime RegisteredDate
		{
			get
			{
				DateTime date;
				DateTime.TryParseExact(Registered, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
				return date.ToUniversalTime();
			}
		}

		public static LastFmUserProfile Deserialize(StreamReader reader)
		{
			XmlSerializer serializer = new XmlSerializer(typeof(LastFmUserProfile));
			LastFmUserProfile profile = (LastFmUserProfile) serializer.Deserialize(reader);
			return profile;
		}
	}
}
