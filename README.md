# CASCHost

CASCHost is a specially designed web service that both builds and hosts modified CASC archives. This web service is designed to replicate Blizzard's own CDN meaning a client works as seamlessly with custom content as with retail.

Requirements:

*  [.Net Core 2.0](https://www.microsoft.com/net/download/core)
* A MySQL server
* A public domain name ( if hosting a public server )

#### Settings: ####
Before using CASCHost the settings found in appsettings.json will need to be adjusted:

* RebuildOnLoad:
	* Enables rebuilding each time the service is launched
* RebuildOnChange: 
	* Enables rebuilding when changes are detected in the *wwwroot\Data folder*
	* Rebuilds happen 30 seconds after the last change is made to avoid file permission issues and misfiring
* MinimumFileDataId:
	* This is the smallest FileDataId for new files; this acts as a buffer between official Blizzard files and custom ones
	* Set this relatively high if planning to upgrade the client in the future as to avoid overwriting future Blizzard FileDataIds
* HostDomain: 
	* This is the public address of this web service
	* This must be "localhost" or a domain, it can not be an IP address but can have a port
* CDNs:
	* These are additional CDNs that contain WoW CASC files such as a custom backup of a particular patch
	* This MUST be a domain, IPs and ports are not accepted by the client
* SqlConnection:
	* SQL connection details used for storing root file information
	* CASCHost will generate the required SQL table automatically
* PatchUrl:
	* This is the live Blizzard patch URL used for downloading required files
* Locale:
	* This is the client locale you'll be targeting, this is important for localised DB2 files to work correctly
* DirectoryHash:
	* This is for internal use only and shouldn't be modified

#### Usage: ####
1. Put the *.build.info* file from the client you are targeting into the *wwwroot\SystemFiles* folder
2. Put your custom files inside the *wwwroot\Data* folder matching the Blizzard folder structure. If you are already using the patched WoW executable you'll have the correct structure already i.e.
    * **wwwroot\Data\Interface\GLUES\MODELS\UI_MainMenu_Legion\UI_MainMenu_Legion.M2**
3. The web service will then generate all the new client files and put them in the *wwwroot\Output* folder
4. All new files will have their information inserted into a a MySQL table named *root_entries* including their FileDataId
5. A patched executable will be produced in the *wwwroot\Output* folder which is required for the updated build to work. See below for distribution/first use


#### Public Hosting/Distribution: ####
To publicly host CASCHost you will need to open the port that this service is running on; by default this is port 80. To change the port you need to edit the *hosting.json* file and before starting CASCHost.

You'll also need to provide a patched WoW executable. This is generated when CASCHost builds the CASC directory and can be found in the Output folder. Before distributing the Trinity connection patcher also needs to be applied.

The patched executable doesn't have to be put into an existing WoW installation as the client will download required files as and when it is needed. If it is put into an existing installation the *.build.info* will need to be deleted otherwise the client may not update and download the new files.

#### Notes: ####
* On the first build the system downloads the files it needs from Blizzard's CDN so may take a few minutes to complete. If this fails, as Blizzard does delete old client versions, you must use CASCExtractor to extract the required files
* The wwwroot/Output folder's files should only ever be removed if something has gone wrong with a build. CASCHost self regulates and removes files when it is safe to do so.
* To force a rebuild you can navigate to the following url ( 'port' being the port CASCHost is running on )
	* **http://localhost:{{port}}/rebuild**
* When a file is deleted it remains in the Output folder for a week before being removed. This is to prevent streaming errors with missing files for the currently online players.
* This may use >2GB of ram as it stores the encoding and root files in memory when rebuilding
* To switch client versions simply clear the SystemFiles and Output folders and put the new .build.info into the SystemFiles folder as per the initial installation. FileDataIds will be restored as they are maintained via the MySQL database table.
