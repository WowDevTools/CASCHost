
# CASCHost

CASCHost is a specially designed web service that both builds and hosts modified a CAS container.
This web service is designed to replicate Blizzard's own CDN meaning a client works as seamlessly with custom content as with retail.

The implementation provided is designed for the 99% whom want to add, edit and remove files from a specific build and host the changes all in one simple-ish application.
For those wanting to create CAS from scratch, have their own build pipeline or want a MPQEditor style tool, you will need to find another solution.

Included is a rough [diagram](CASC_Diagram.svg) of how CAS works to attempt to explain what CASCHost does and how CAS works at a very fundamental level. Further information can be found on the [WoWDev Wiki](https://wowdev.wiki/CASC).

#### Requirements: ####

*  [.Net Core 2.1](https://www.microsoft.com/net/download/core)
* A MySQL server (version 5.6+)
* A public domain name (if hosting a public server)

#### Settings: ####
Before using CASCHost the settings found in `appsettings.json` will need to be adjusted:

* MinimumFileDataId:
	* This is the smallest fileDataId for new files; this acts as a buffer between official Blizzard files and custom ones to avoid collisions
	* I'd advise setting this relatively high as to avoid colliding with future Blizzard fileDataIds in the eventuality of moving to a newer build
* RebuildPassword
	* Sets the password required for the rebuild command
* BNetAppSupport:
	* This enables the creation of the Install and Download files which are only required if you're deploying your client via the B-Net App
* StaticMode:
	* Create under wwwroot\output the original Blizzard CDN structure to host a CDN with Apache or nginx (This option disable all CASCHost hosting features)
* HostDomain: 
	* This is the public address of this web service
	* Note: This must be "localhost" or a domain, IP addresses are NOT supported however ports ARE
* CDNs:
	* These are additional CDNs that contain WoW CASC files such as a backup of a particular build (in the case of Blizzard's taking theirs down)
	* Note: This MUST be a domain, IP addresses and ports are not accepted by the client
* SqlConnection:
	* SQL connection details used for storing root file information, CASCHost automates table creation
* PatchUrl:
	* This is the live Blizzard patch URL used for downloading required files
* Locale:
	* This is the client locale you'll be targeting, this is important for localised DB files to work correctly
* DirectoryHash:
	* This is for internal change tracking only

#### Usage: ####
1. Put the `.build.info` file from the client you are targeting into the `wwwroot\systemfiles` folder
2. Put your custom files inside the `wwwroot\data` folder matching the Blizzard folder structure. If you are already using the patched WoW executable you'll have the correct structure already e.g. `wwwroot\data\interface\glues\models\ui_mainmenu_legion\ui_mainmenu_legion.m2`
3. The web service will then generate all the new client files and put them in the `wwwroot\output` folder
4. All new files will have their information inserted into a a MySQL table named `root_entries` which includes their fileDataId - this is the Id referenced by the game files and DBs
5. You will need to patch your exe/app to point the Versions and CDNs URLs to your CASCHost server (ask in Discord if you need information on how to do this)
6. Whenever you change a file simply use the rebuild command (see notes), wait for it to finish and restart the client

#### Public Hosting/Distribution: ####
To publicly host CASCHost you will need to open the port that this service is running on; by default this is port 5100. To change the port you need to edit the `hosting.json` file and restart CASCHost.

You'll also need to provide a patched WoW executable - see step 5 of Usage, the Trinity connection patcher needs to be applied as usual.

The patched executable doesn't have to be put into an existing WoW installation as the client will download the files it needs. If it is put into an existing installation the `.build.info` will need to be renamed/deleted to force the client to connect to your CDN opposed to Blizzard's.

#### Notes: ####
* On the first build the system downloads the files it needs from Blizzard's CDN so may take a few minutes to complete. If this fails, as Blizzard does delete old client versions, you must use CASCExtractor to extract the required files.
* The `wwwroot/output` folder's files should only ever be removed if something has gone wrong. CASCHost self regulates and removes files when it is appropiate to do so.
* To force a rebuild you can navigate to the following url
	* `http://localhost:CASCHOST_PORT/rebuild`
	* `http://localhost:CASCHOST_PORT/rebuild/CASCHOST_REBUILDPASSWORD`
* When a file is deleted it remains in the `wwwroot/output` folder for a week before being removed. This is to prevent streaming errors with missing files for the currently online players.
* This may use >2GB of ram as it stores the encoding and root files in memory when rebuilding
* To switch client versions simply clear the `wwwroot/systemfiles` and `wwwroot/output` folders and overwrite the `wwwroot/systemfiles` `.build.info` with the new build's one. The fileDataIds will be restored and the files regenerated the next time CASCHost is run.
* NEVER delete the database, this is fundamental to maintaining the same fileDataIds each rebuild!

#### FAQ: ####
What versions are supported?
- Everything WoD+.

It was working now its all broken - what do?
- Delete the `Output` folder and restart CASCHost this will resolve 99% of all issues. Failing that, jump on Discord.

I keep getting a stream error. What is this?
- Either a file is malformed or doesn't exist (either in CASCHost or the Blizzard CDN).

How can I see the fileDataIds that are generated?
- In the database there will be a table called `root_entries` which contains everything needed for modding including the fileDataId.

Isn't putting everything in the `wwwroot` folder unsecure?
- No. There are special rooting rules that prevent access to the `Data` directory.

#### Thanks: ####
- [TOM_RUS](https://github.com/tomrus88) for his [CASCExplorer](https://github.com/WoW-Tools/CASCExplorer) implementation
- The [WoWDev Wiki](https://wowdev.wiki/CASC) contributors
- furl for sharing his findings with me
- Azarchius and [Epsilon WoW](https://www.epsilonwow.net/) for their months of testing, bug fixes and feature/implementation suggestions
- Helnesis and [Kuretar](http://kuretar-serveur.fr/) for their time testing
- Various others for testing and taking on the development and support of this project
