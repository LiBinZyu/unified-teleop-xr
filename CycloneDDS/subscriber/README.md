# XR Subscriber

The gateway serves the viewer and the live Cyclone DDS stream. Viewers open the gateway URL in a WebGL-capable browser.

## First-time setup

Run these commands in `CycloneDDS/subscriber` on the machine that will receive DDS traffic. Rebuild after changing the viewer's JavaScript or bundled assets.

```sh
npm ci
npm run build
```

## Start each session

```sh
python bridge.py --domain 0
```

Match the publisher's DDS domain and open the printed URL, normally `http://127.0.0.1:8765/`. Keep the gateway running while viewing. To share with other devices, add `--bind <gateway-lan-ip>` and give viewers the printed URL. Use `--peer <publisher-ip>` for unicast discovery or `--config <file.xml>` for custom DDS settings.

The gateway uses Python 3.10+ and the native Cyclone DDS plugin. Windows x64 stages the repository's DLLs into `runtime/` on first launch. The LAN URL serves live data to anyone who can reach it.

## Controls

- Drag to orbit, scroll to zoom, and right-drag to pan. **Fit view** centers the visible objects. ROS axes use meters.
- Select the controller model above the image. The overlay shows live values, including zero and negatives, with separate X and Y stick readings.
- Toggle **Data Monitor** to open or close it. Select fields to plot. Set **Time** to 1–120 seconds and leave **Y min** or **Y max** blank for automatic bounds.
- Views and overlays disappear when a topic goes offline. Their switches re-enable on valid data. Use `--stale-ms` to change the default one-second timeout.

## Build the gateway on Linux or macOS

Build the native plugin with the repository's message definitions and Cyclone DDS 0.10.4. The following installs both libraries locally.

```sh
git clone --depth 1 --branch 0.10.4 https://github.com/eclipse-cyclonedds/cyclonedds.git native-build/cyclone-src
cmake -S native-build/cyclone-src -B native-build/cyclone -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=OFF
cmake --build native-build/cyclone --config Release
cmake --install native-build/cyclone --config Release --prefix runtime/cyclone

cmake -S . -B native-build/plugin -DCMAKE_BUILD_TYPE=Release -DCMAKE_PREFIX_PATH="$PWD/runtime/cyclone"
cmake --build native-build/plugin --config Release
cmake --install native-build/plugin --config Release --prefix runtime
```

Use `python3 bridge.py` on these hosts. To load an existing compatible plugin, pass `--library <path>`.

Source assets are in `assets/`. Controller renders come from XR Configurator. Masks use the supplied SVGs. Hand models and the reference driver come from Unity XR Hands 1.10.0. The WebGL hand shader uses the Unity XR Interaction Toolkit hand graphs, finger mask and `Unity_Hand_Medium.mat` in `assets/reference/`.
