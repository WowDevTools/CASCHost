using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Net;

namespace CASCHost
{
	public class FileProvider : IFileProvider
	{
		private readonly string outPath;
		private readonly IServiceProvider serviceProvider;
		private IHttpContextAccessor contextAccessor;

		public FileProvider(IHostingEnvironment env, IServiceProvider provider)
		{
			serviceProvider = provider;
			outPath = Path.Combine(env.WebRootPath, "Output");
		}


		public IFileInfo GetFileInfo(string subpath)
		{
			string fullPath = Path.Combine(outPath, Path.GetFileName(subpath));

			if (ProcessCommands(subpath))
				return new NotFoundFileInfo(fullPath);

			if (GetByteRange(out RangeItemHeaderValue range))
				Startup.Logger.LogConsole($"Partial Content (206) {Path.GetFileName(subpath)}, Content-Range: {range.From} - {range.To}");

			return new PhysicalFileInfo(new FileInfo(fullPath));
		}


		#region Extended Functionality

		private bool ProcessCommands(string path)
		{
			IHttpContextAccessor contextAccessor = serviceProvider.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor;
			ConnectionInfo conn = contextAccessor?.HttpContext.Connection;
			string[] parts = path.TrimStart('/').Split('/');

			if (conn != null && parts.Length > 0)
			{
				bool isLocal = conn?.LocalIpAddress.ToString() != "::1" ? conn.RemoteIpAddress.Equals(conn.LocalIpAddress) : IPAddress.IsLoopback(conn.RemoteIpAddress);
				if (!isLocal)
					return false;

				switch (Path.GetFileName(parts.First()).ToLowerInvariant())
				{
					case "rebuild":
						string pass = parts.Length > 1 ? parts.Last() : "";
						if (Startup.Settings.RebuildPassword == pass)
							Startup.Watcher.ForceRebuild();
						return true;
				}
			}

			return false;
		}


		private bool GetByteRange(out RangeItemHeaderValue range)
		{
			if (contextAccessor == null)
				contextAccessor = serviceProvider.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor;

			range = contextAccessor?.HttpContext?.Request?.GetTypedHeaders()?.Range?.Ranges.FirstOrDefault();
			return range != null;
		}

		#endregion

		#region Unused
		public IChangeToken Watch(string filter) => throw new NotImplementedException();

		public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
		#endregion
	}
}
