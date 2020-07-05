# Easy LOU

![EasyLOU Screenshot](Screenshot.png?raw=true "EasyLOU Screenshot")


## So what is EasyLOU?
EasyLOU is a totally free tool inspired by EasyUO, the popular premiere macroing tool for Ultima Online. 

## Why EasyLOU and not EasyLOA?
For two reasons:  
- The name EasyLOA was already taken by https://github.com/MythikGN/EasyLoA :)
- As a tribute to the [Legends of Ultima](https://www.legendsofultima.online/) community server.    

## What can I do with EasyLOU?
With EasyLOU you can write [Lua 5.2](https://www.lua.org/manual/5.2/) scripts that will make your Legends of Ultima character perform pretty much anything you want them to.

## How do I get started?
Download the pre-built binary from the Releases tab, and place it in the same folder where your Assembly-CSharp.dll is (usually something like C:\Program Files\Legends of Aria Launcher\Legends of Aria\Legends of Aria_Data\Managed).  
Launch EasyLOU.exe. On Windows 10, you might get a "Microsoft Defender SmartScreen prevented an unrecognised app from starting." error message. If you know what you're doing, open the properties menu of EasyLOU.exe, and check the "Unblock" click apply, click OK, and relaunch EasyLOU.exe.    
Once EasyLOU is launched, then click on the "New CLI" button, wait for the client to display the login page, then press OK to inject.  
Take a look at the scripts that can be found in the [Examples/](Examples/) folder.  
Take a look at the available [Commands](COMMANDS.md) and [Variables](VARIABLES.md).  
Learn some [Lua](https://www.lua.org/manual/5.2/).  
Enjoy.

## Is EasyLOU compatible with the official Legends of Aria servers?
The community server Legends of Ultima is the reason why this tool exists: we enjoy this community server and have decided to write this tool and name EasyLOU after it.  
EasyLOU has only been tested with the Legends of Ultima Community Server as of today.  
EasyLOU may or may not work with official Legends of Aria servers run by Citadel Studios.  

## Why is EasyLOU not working with the new version of Legends of Aria?
Some Legends of Aria client updates may break the compatibility with EasyLOU.  
As of today, EasyLOU is compatible with Legends of Aria 0.9.7 and we do not know if we will be able to always keep EasyLOU up-to-date with the latest client patch.  
Volunteers are always welcome :)

## How does it work?

Launch EasyLOU.  
Click on the "New Client" icon on the toolbar and select the "Legends of Aria.exe" file which is usually installed under C:\Program Files\Legends of Aria Launcher\Legends of Aria.  
Wait for Legends of Aria Client to be loaded and for the Login page to appear.  
Click on the "OK" button on EasyLOU and make sure the status bar at the bottom of the EasyLOU window says "Connected".  
Load a script.  
Enjoy :)

## Yes, ok, but how does it *really* work?

EasyLOU.exe injects LOU.dll into the Legends of Aria client process, using a well known technique for injecting assemblies into Mono embedded applications, commonly Unity Engine based games.  
EasyLOU.exe acts as the GUI while LOU.dll acts as the commands engine, and they communicate via two shared memory maps:  
- A ClientCommand memory map, where EasyLOU.exe queues all the commands that have to be processed and executed by the LoA Client
- A ClientStatus memory map, where LOU.dll updates the status of the LoA Client and the answers to various commands by populating a bunch of variables  
Credits for various components and implementations can be found at the bottom of this page.  


## Can I run multiple scripts at the same time?

Yes, but it has not been extensively tested yet.

## Can I multibox?

Yes.  
You need to open one instance of EasyLOU, lunch a client, inject it, and wait for EasyLOU to connect to it.  
Then you can open another instance of EasyLOU, lunch another client, inject it, and wait for EasyLOU to connect to it.  
Rinse, and repeat.  

## How can I build EasyLOU?

EasyLOU has been compiled and tested with .NETFramework Version v4.7.2.
In order to build it, you need a copy of the Legends of Aria client, and you need to copy into the LOU\libs\ folder the following libraries from the C:\Program Files\Legends of Aria Launcher\Legends of Aria\Legends of Aria_Data\Managed folder (or whatever path you have installed your client to):

Assembly-CSharp-firstpass.dll  
Assembly-CSharp.dll  
CoreUtil.dll  
MessageCore.dll  
protobuf-net.dll
UnityEngine.CoreModule.dll  
UnityEngine.InputLegacyModule.dll  
UnityEngine.InputModule.dll  
UnityEngine.PhysicsModule.dll  
UnityEngine.UI.dll  

# IMPORTANT DISCLAIMER

By using EasyLOU you may be breaching the Terms and Conditions of Citadel Studios, Legends of Aria, Legends of Ultima, or whatever community server you are playing on or service you are using.

***USE AT YOUR OWN RISK***

# IMPORTANT RECOMMENDATIONS

ONLY download EasyLOU from its official repository: NEVER accept a copy of EasyLOU from other people.  
ONLY run scripts that you can understand: NEVER accept scripts from people you do not know and trust.  

Keep in mind, there is always a possibility that a malicious version of EasyLOU or a malicious script will steal your LOU/LOA or Steam credentials.

Also: don't be evil.

***USE AT YOUR OWN RISK***

# CREDITS

LadyBinary is a tribute to Lord Binary, who was very much active in the UO hacking scene (see for example [UO_RICE](https://github.com/necr0potenc3/UO_RICE)).  
EasyLOU is of course inspired from the great [EasyUO](http://www.easyuo.com/).  
EasyLOU is a tribute to [Legends Of Ultima Online](https://www.legendsofultima.online/), whose passionate staff have dedicated so much effort in putting together a wonderful product based off of [Legends of Aria](https://www.legendsofaria.com/).  
The LUA engine is based on [MoonSharp](https://github.com/moonsharp-devs/moonsharp/), commit 4e748a7.  
The Text editor is based on [ICSharpCode.TextEditorEx](https://github.com/StefH/ICSharpCode.TextEditorEx.git), commit 1934da7.  
The Mono Injection is based on [SharpMonoInjector](https://github.com/warbler/SharpMonoInjector), commit 73566c1.  

# CONTACTS

You can contact me at ladybinary@protonmail.com

License
-------

This project is licensed under the MIT license, which can be found in the file
[LICENSE](LICENSE) in the root of the project source code.  
