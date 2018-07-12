using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.IO;
using System.Net.Http;
using System.Net;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace CASCHost
{
	public class Startup
	{
		public static AppSettings Settings { get; private set; }
		public static Logger Logger { get; private set; }
		public static Cache Cache { get; private set; }
		public static DataWatcher Watcher { get; private set; }

		public Startup(ILoggerFactory loggerFactory)
		{
			Logger = new Logger(loggerFactory.CreateLogger<Startup>());
		}

		public void ConfigureServices(IServiceCollection services)
		{
			var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
			IConfigurationRoot configuration = builder.Build();

			services.AddOptions();
            services.AddHttpContextAccessor();
            services.AddSingleton<FileProvider>();
            services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
		}

		public void Configure(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider serviceProvider, IOptions<AppSettings> settings)
		{
			if (env.IsDevelopment())
				app.UseDeveloperExceptionPage();

            //Set file handler
            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = serviceProvider.GetService<FileProvider>(),
				DefaultContentType = "application/octet-stream",
				ServeUnknownFileTypes = true
			});

			//Load settings
			Settings = settings.Value;

			//Create directories
			Directory.CreateDirectory(Path.Combine(env.WebRootPath, "Data"));
			Directory.CreateDirectory(Path.Combine(env.WebRootPath, "Output"));
			Directory.CreateDirectory(Path.Combine(env.WebRootPath, "SystemFiles"));

			//Check installation is corect
			StartUpChecks(env);

			//Load cache
			Cache = new Cache(env);

			//Start DataWatcher
			Watcher = new DataWatcher(env);
		}



		private void StartUpChecks(IHostingEnvironment env)
		{
			bool exit = false;
			const string DOMAIN_REGEX = @"^(?:.*?:\/\/)?(?:[^@\n]+@)?(?:www\.)?([^\/\n]+)";

			//Normalise values
			Settings.PatchUrl = Settings.PatchUrl.TrimEnd('/');
			Settings.HostDomain = Settings.HostDomain.TrimEnd('/');
			Settings.Save(env);

			//.build.info check
			if (!File.Exists(Path.Combine(env.WebRootPath, "SystemFiles", ".build.info")))
			{
				Logger.LogCritical("Missing .build.info.");
				exit = true;
			}

			//Validate the domain name - must be a valid domain or localhost
			bool hasProtocol = Settings.HostDomain.ToLowerInvariant().Contains("://");
			if (IPAddress.TryParse(Settings.HostDomain, out IPAddress address))
			{
				Logger.LogCritical("HostDomain must be a domain Name.");
				exit = true;
			}
			else if (hasProtocol || !Regex.IsMatch(Settings.HostDomain, DOMAIN_REGEX + "$", RegexOptions.IgnoreCase))
			{
				string domain = Regex.Match(Settings.HostDomain, DOMAIN_REGEX, RegexOptions.IgnoreCase).Groups[1].Value;
				Logger.LogCritical($"HostDomain invalid expected {domain.ToUpper()} got {Settings.HostDomain.ToUpper()}.");
			}

			//Validate offical patch url
			if (!Uri.IsWellFormedUriString(Settings.PatchUrl, UriKind.Absolute))
			{
				Logger.LogCritical("Malformed Patch Url.");
				exit = true;
			}
			else if (!PingPatchUrl())
			{
				Logger.LogCritical("Unreachable Patch Url.");
				exit = true;
			}

			if (exit)
			{
				Logger.LogCritical("Exiting...");
				System.Threading.Thread.Sleep(3000);
				Environment.Exit(0);
			}
		}

		private bool PingPatchUrl()
		{
			try
			{
				using (var clientHandler = new HttpClientHandler() { AllowAutoRedirect = false })
				using (var webRequest = new HttpClient(clientHandler) { Timeout = TimeSpan.FromSeconds(3) })
				using (var request = new HttpRequestMessage(HttpMethod.Head, Settings.PatchUrl + "/versions"))
				using (var response = webRequest.SendAsync(request).Result)
					return response.StatusCode == HttpStatusCode.OK;
			}
			catch
			{
				return false;
			}
		}
	}
}
