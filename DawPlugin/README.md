# Building

1. Install CMake
2. `cd` to `DawPlugin` folder.
3. Run `cmake -B build -S . -DCMAKE_BUILD_TYPE=Debug` to generate build files.
4. Run `cmake --build build` to build the plugin.
5. Add `path/to/OpenUtau/DawPlugin/build/bin` to your DAW's plugin search path.

Notes:
- `-DCMAKE_BUILD_TYPE=Debug` can be replaced with `-DCMAKE_BUILD_TYPE=Release` for a release build.
- When you're using Visual Studio, your plugin will be built in `build/x64-Debug` or `build/x64-Release` instead.
- This plugin is very unstable! Don't forget to save your work often.
