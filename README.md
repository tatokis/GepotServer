# Server implementation for "Classic 8-Ball Multiplayer"

This project is a server-side implementation for the flash game "Classic 8-Ball Multiplayer", found at https://www.speeleiland.nl/classic-8-ball-multiplayer.htm.

![Screenshot of the game connected to this server implementation](https://github.com/tatokis/GepotServer/blob/master/screenshot.png?raw=true)

## How to run the server
Open the project in MonoDevelop or Visual Studio and click run. Currently there are no binaries provided.

This has been tested under Mono 6.12, but it should run on much earlier versions of both Mono and .NET.

## How to run the game - client
### Warning: At the time of writing, Ruffle does not support certain features used by this flash, rendering it unable to connect to servers.

The swf file is not provided in this repository. It can be downloaded from [here](https://web.archive.org/web/20220822091438if_/https://media.jaludo.com/oldmpgames/speeleiland_nl/8ball/sdjkl2389asl19.swf).

The standalone Flash Player first needs to be allowed to connect to the internet. Unfortunately, clicking the "Settings" button in the prompt simply opens a 404 page, meaning it can not be configured that way.

Instead, [.minerva](https://github.com/gmariani/minerva) can be used. A live instance of it can be found at https://mariani.life/projects/minerva/.


1. Go to the .minerva website, or run it locally.
2. Click on Open and select the `settings.sol` file.
   > On Linux, it is located at `~/.macromedia/Flash_Player/macromedia.com/support/flashplayer/sys/settings.sol`.
   >
   > If it doesn't exist, make sure you have ran `flashplayer` at least once.
3. Select `crossdomainAllow` from the left panel.
4. On the right panel, change the slider to `YES`.
5. Repeat steps 3 & 4 for `crossdomainAlways`.
6. Click on Save and overwrite the `settings.sol` file at the same location.

It might be possible to distribute a preconfigured `settings.sol` with a specific domain name allowed only, however this has not been investigated.

Once this has been configured, the game can now be ran using the standalone `flashplayer` binary.
Either navigate to the path containing the swf using a terminal and run
```
flashplayer "file:///$PWD/sdjkl2389asl19.swf?surl=localhost&sport=6890&user=Desired%20Username"
```
or open the standalone Flash Player and manually fill in the URL with the required parameters.
