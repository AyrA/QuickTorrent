# QuickTorrent
Minimalistic torrent client for people who don't torrent frequently

**This application is not meant to be used often. The download algorithm is not very efficient.**
This application is primarily meant to be used by people who do not want to install a torrent client.

## Building

Clone this repository and build. I used VS2015 Community edition.
To run it you need [MonoTorrent](https://github.com/mono/monotorrent).

## Downloading

There are no releases yet but I will do one shortly.

## Using

This is a command line tool. There are 2 ways of using it:

### Command line arguments

You can supply any number of command line arguments.
In this case the application detects if you meant a magnet link, file name or info hash.
File names have precedence over info hashes.
This is usually not a problem because torrent files end in `.torrent` which is not part of an infohash.
To just download a torrent file, drag it onto the executable.

### File name

You can name the application in the format `<InfoHash>.exe` (without brackets) and it will use that hash for downloading.
This is only used if no arguments are provided.

## Interface

The interface is very crude at the moment:

- `↑↓`: Select menu entry.
- `ENTER`: Show transfer details.
- `SPACE`: Start/Stop a transfer
- `ESC`: Close details view. Shutdown all transfers and exit on main view.

All downloads are initially started and the mode is not saved when exiting.

The transfer details shows:

- Larger progress map
- Progress percentage
- Full torrent name
- Info hash
- Number of files
- Total size
- Torrent state

The entries in the transfer list and details view are color coded:

- **blue**: This torrent is complete and is seeding
- **green**: This torrent is downloading
- **cyan**: This torrent is checking the local files for integrity
- **yellow**: This torrent is searching for metadata in the DHT network
- **red**: This torrent has an error. Usually happens because the output file(s) are in use otherwise.
- **gray**: This torrent is stopped. Use menu actions to start it if you want.
- **dark gray**: This torrent is paused. *This achievement is currently unobtainable*

![UI Demo](https://i.imgur.com/LJqJUnF.png)

## TODO

Here is the basic todo/feature list. This is not necessarily in the order it will be implemented

- [X] Basic torrent handling
- [X] Get DHT to work properly
- [X] Piecemap
- [X] Process command line arguments
- [X] Handle multiple torrents at once
- [ ] Join multiple instances into one
- [ ] Possibility for configuration (download path, ports, speeds)
- [ ] Better User interface
- [ ] Resolve bug with torrents stuck in "green" state but have all pieces.
- [X] Fast resume

### Current limitations

With the state the TODO list is in, the solution has these limitations:

- Saves metadata and configuration/state files in APPDATA (non portable)
- Hardcoded Port for connections (`54321`) and connection count
- Only one instance at the moment ~~with a single torrent~~
- All speeds are unrestricted
- Hardcoded download path: User download folder
- Only very crude interface.
