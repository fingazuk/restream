# Restream
## Usage : restream

- Re-stream an MPEG-TS M3U playlist with a self hosted proxy server
- Ideal for running on a home server permanently connected to a VPN, freeing up other devices from needing a VPN connection to access the playlist (if one is normally required)
- Allows multiple devices to play the same playlist entry at the same time (MaxConnections settings)
- Copy settings-example.json to settings.json in the build directory and set the playlist URL to your own URL
- Set the port setting if required (default 3666)
- Open a media player with http://localhost:3666 (on the same machine) or http://<serverip.address>:3666 on any other machine to access the local version of the playlist