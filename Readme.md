# Restream
## Usage : restream

- Re-stream an MPEG-TS M3U playlist with a self hosted server
- Ideal for running on a server connected to a VPN, freeing up other devices on the same network from needing a VPN connection to access the playlist (if one is normally required)
- Allows concurrent connections to the same single remote stream
- Required : Copy settings-example.json to settings.json in the build directory and configure the remote playlist URL
- Optional : Configure the servers port, default is 3666
- Optional : Configure the max number of client connections to the server
- Optional : Configure the white list to filter the playlist for names eg. [ "NEWS" , "SPORT" , "MUSIC" ]
- Open a media player with http://localhost:3666 on the same machine to start streaming 