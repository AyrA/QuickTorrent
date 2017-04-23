# QuickTorrent
Minimalistic torrent client for people who don't torrent frequently

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

### File name

You can name the application in the format `<InfoHash>.exe` (without brackets) and it will use that hash for downloading

## TODO

Here is the basic todo/feature list. This is not necessarily in the order it will be implemented

- [X] Basic torrent handling
- [X] Get DHT to work properly
- [X] Piecemap
- [X] Process command line arguments
- [X] Handle multiple torrents at once
- [ ] Join multiple instances into one
- [ ] Possibility for configuration (download path, ports, speeds)
- [ ] User interface
- [ ] Fast resume

### Current state

With the state the TODO list is in, the solution has these limitations:

- Saves metadata and configuration/state files in APPDATA (non portable)
- Hardcoded Port for connections (`54321`)
- Only one instance at the moment ~~with a single torrent~~
- All speeds are unrestricted
- Saves torrents in the users download folder
- You have to wait for it to scan the entire torrent content if you load a partially downloaded torrent.
- No user interface. Just shows progress of downloads. Close with `ESC`
