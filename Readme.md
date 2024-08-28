# Restream
## Usage : restream

- Re-stream an MPEG-TS M3U playlist with a self hosted proxy server
- Ideal for running on a home server permanently connected to a VPN, freeing up other devices from needing a VPN connection to access the playlist (if one is normally required)
- Allows multiple devices to play the same playlist entry at the same time (MaxConnections setting)
- Copy settings-example.json to settings.json in the build directory and configure the PlaylistURL setting
- Configure the port if required (default server port is 3666)
- Open a media player with http://localhost:3666 (on the same machine) or http://<serverip.address>:3666 on any other machine to access the proxy servers playlist