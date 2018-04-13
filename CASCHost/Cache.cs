using CASCEdit;
using CASCEdit.Configs;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using Microsoft.AspNetCore.Hosting;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CASCHost
{
	public class Cache : ICache
	{
		public string Version { get; private set; }
		public HashSet<string> ToPurge { get; private set; }
		public IReadOnlyCollection<CacheEntry> Entries => RootFiles.Values;
		public uint MaxId => RootFiles.Values.Count == 0 ? 0 : RootFiles.Values.Max(x => x.FileDataId);

		public bool HasFiles => RootFiles.Count > 0;
		public bool HasId(uint fileid) => RootFiles.Any(x => x.Value.FileDataId == fileid);

		private IHostingEnvironment env;
		private string Patchpath => Path.Combine(CASCContainer.Settings.OutputPath, ".patch");
		private Dictionary<string, CacheEntry> RootFiles;
		private Queue<string> queries = new Queue<string>();
		private bool firstrun = true;


		public Cache(IHostingEnvironment environment)
		{
			env = environment;
			Startup.Logger.LogInformation("Loading cache...");
			Load();
		}


		public void AddOrUpdate(CacheEntry item)
		{
			if(firstrun)
			{
				Clean();
				firstrun = false;
			}

			if (RootFiles == null)
				Load();

			// Update value
			if (RootFiles.ContainsKey(item.Path))
			{
				// Matching
				if (RootFiles[item.Path] == item)
					return;

				RootFiles[item.Path] = item;

				queries.Enqueue(string.Format(REPLACE_RECORD, MySqlHelper.EscapeString(item.Path), item.FileDataId, item.Hash, item.MD5, item.BLTE));
				return;
			}

			// Matching Id - Ignore root/encoding
			if (item.FileDataId > 0 && RootFiles.Values.Any(x => x.FileDataId == item.FileDataId))
			{
				var existing = RootFiles.Where(x => x.Value.FileDataId == item.FileDataId).ToArray();
				foreach (var ex in existing)
				{
					queries.Enqueue(string.Format(DELETE_RECORD, MySqlHelper.EscapeString(item.Path)));
					RootFiles.Remove(ex.Key);
				}
			}

			// Add
			RootFiles.Add(item.Path, item);

			queries.Enqueue(string.Format(REPLACE_RECORD, MySqlHelper.EscapeString(item.Path), item.FileDataId, item.Hash, item.MD5, item.BLTE));
		}

		public void Remove(string file)
		{
			if (RootFiles.ContainsKey(file))
			{
				queries.Enqueue(string.Format(DELETE_RECORD, MySqlHelper.EscapeString(RootFiles[file].Path)));
				RootFiles.Remove(file);
			}
		}

		public void Save()
		{
			BatchTransaction();
		}

		public void Load()
		{
			if (RootFiles != null)
				return;

			RootFiles = new Dictionary<string, CacheEntry>();
			LoadOrCreate();
		}

		public void Clean()
		{
			//Delete previous Root and Encoding
			if (RootFiles.ContainsKey("__ROOT__") && File.Exists(Path.Combine(CASCContainer.Settings.OutputPath, RootFiles["__ROOT__"].BLTE.ToString())))
				File.Delete(Path.Combine(CASCContainer.Settings.OutputPath, RootFiles["__ROOT__"].BLTE.ToString()));
			if (RootFiles.ContainsKey("__ENCODING__") && File.Exists(Path.Combine(CASCContainer.Settings.OutputPath, RootFiles["__ENCODING__"].BLTE.ToString())))
				File.Delete(Path.Combine(CASCContainer.Settings.OutputPath, RootFiles["__ENCODING__"].BLTE.ToString()));
		}


		#region SQL Methods
		private void LoadOrCreate()
		{
			Version = new SingleConfig(Path.Combine(env.WebRootPath, "SystemFiles", ".build.info"), "Active", "1")["Version"];
			using (MySqlConnection connection = new MySqlConnection(Startup.Settings.SqlConnection))
			using (MySqlCommand command = new MySqlCommand())
			{
				connection.Open();
				command.Connection = connection;

				// create data table
				command.CommandText = CREATE_DATA_TABLE;
				command.ExecuteNonQuery();

				// load data
				command.CommandText = LOAD_DATA;
				ReadAll(command.ExecuteReader());

				// purge old data
				command.CommandText = PURGE_RECORDS;
				command.ExecuteNonQuery();
			}
		}

		private void ReadAll(DbDataReader reader)
		{
			ToPurge = new HashSet<string>();

			using (reader)
			{
				while (reader.Read())
				{
					CacheEntry entry = new CacheEntry()
					{
						Path = reader.GetFieldValue<string>(1),
						FileDataId = reader.GetFieldValue<uint>(2),
						Hash = reader.GetFieldValue<ulong>(3),
						MD5 = new MD5Hash(reader.GetFieldValue<string>(4).ToByteArray()),
						BLTE = new MD5Hash(reader.GetFieldValue<string>(5).ToByteArray())
					};

					//Keep files that still exist or are special and not flagged to be deleted
					bool keep = File.Exists(Path.Combine(env.WebRootPath, "Data", entry.Path)) && reader.IsDBNull(6);
					if (keep || entry.FileDataId == 0)
					{
						RootFiles.Add(entry.Path, entry);
					}
					else if (reader.IsDBNull(6)) // needs to be marked for purge
					{
						queries.Enqueue(string.Format(DELETE_RECORD, MySqlHelper.EscapeString(entry.Path)));
						Startup.Logger.LogInformation($"{entry.Path} missing. Marked for removal.");
						ToPurge.Add(entry.Path);
					}
					else if (reader.GetFieldValue<DateTime>(6) <= DateTime.Now.Date) // needs to be purged
					{
						ToPurge.Add(entry.Path);

						string filepath = Path.Combine(env.WebRootPath, "Output", entry.BLTE.ToString());
						if (File.Exists(filepath))
							File.Delete(filepath);
						if (File.Exists(Path.Combine(env.WebRootPath, "Data", entry.Path)))
							File.Delete(Path.Combine(env.WebRootPath, "Data", entry.Path));
					}
				}

				reader.Close();
			}

			BatchTransaction();
		}

		private void BatchTransaction()
		{
			if (queries.Count == 0)
				return;

			Startup.Logger.LogInformation("Bulk updating database.");

			StringBuilder sb = new StringBuilder();
			while (queries.Count > 0)
			{
				sb.Clear();

				int count = Math.Min(queries.Count, 2500); // limit queries per transaction
				for (int i = 0; i < count; i++)
					sb.AppendLine(queries.Dequeue());

				try
				{
					using (MySqlConnection connection = new MySqlConnection(Startup.Settings.SqlConnection))
					using (MySqlCommand command = new MySqlCommand(sb.ToString(), connection))
					{
						connection.Open();
						command.ExecuteNonQuery();
					}
				}
				catch (MySqlException ex)
				{
					Startup.Logger.LogError("SQLERR: " + ex.Message);
				}
			}
		}

		#endregion

		#region SQL Strings

		private const string CREATE_DATA_TABLE = "SET GLOBAL innodb_file_format=Barracuda;                        " +
												 "SET GLOBAL innodb_file_per_table=ON;                            " +
												 "SET GLOBAL innodb_large_prefix=ON;                              " +
												 "                                                                " +
												 "CREATE TABLE IF NOT EXISTS `root_entries` (                     " +
												 " `Id` BIGINT NOT NULL AUTO_INCREMENT,                           " +
												 " `Path` VARCHAR(1024),                                          " +
												 " `FileDataId` INT UNSIGNED,                                     " +
												 " `Hash` BIGINT UNSIGNED,                                        " +
												 " `MD5` VARCHAR(32),                                             " +
												 " `BLTE` VARCHAR(32),                                            " +
												 " `PurgeAt` DATE NULL,                                           " +
												 " PRIMARY KEY(`Id`),                                             " +
												 " UNIQUE INDEX `Path` (`Path`)                                   " +
												 ") COLLATE = 'utf8_general_ci' ENGINE=InnoDB ROW_FORMAT=DYNAMIC; ";

		private const string LOAD_DATA =      "SELECT * FROM `root_entries`;";

		private const string REPLACE_RECORD = "REPLACE INTO `root_entries` (`Path`,`FileDataId`,`Hash`,`MD5`,`PurgeAt`,`BLTE`) VALUES ('{0}', '{1}', '{2}', '{3}', NULL, '{4}'); ";

		private const string DELETE_RECORD =  "UPDATE `root_entries` SET `PurgeAt` = DATE_ADD(CAST(NOW() AS DATE), INTERVAL 1 WEEK) WHERE `Path` = '{0}'; ";

		private const string PURGE_RECORDS =  "DELETE FROM `root_entries` WHERE `PurgeAt` < CAST(NOW() AS DATE); ";

		#endregion

	}
}
