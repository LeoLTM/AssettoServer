# AssettoServer

## Development

### Build the server

```bash
# Linux
dotnet publish -c Release -r linux-x64
# Windows
dotnet publish -c Release -r win-x64
```

### Only build a plugin

```bash
# Linux
dotnet build AssettoServer.Plugins.<PluginName> -c Release -r linux-x64
# Windows
dotnet build AssettoServer.Plugins.<PluginName> -c Release -r win-x64
```

## About
AssettoServer is a custom game server for Assetto Corsa developed with freeroam in mind. It greatly improves upon the default game server by fixing various security issues and providing new features like AI traffic and dynamic weather.

Race and Qualification sessions are in an experimental state and might need more testing.

This is a fork of https://github.com/compujuckel/AssettoServer.

## License
AssettoServer is licensed under the GNU Affero General Public License v3.0, see [LICENSE](https://github.com/compujuckel/AssettoServer/blob/master/LICENSE) for more info.  
Additionally, you must preserve the legal notices and author attributions present in the server.

```
Copyright (C)  2025 Niewiarowski, compujuckel

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published
by the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.


Additional permission under GNU AGPL version 3 section 7

If you modify this Program, or any covered work, by linking or combining it 
with the Steamworks SDK by Valve Corporation, containing parts covered by the
terms of the Steamworks SDK License, the licensors of this Program grant you
additional permission to convey the resulting work.

Additional permission under GNU AGPL version 3 section 7

If you modify this Program, or any covered work, by linking or combining it 
with plugins published on https://www.patreon.com/assettoserver, the licensors
of this Program grant you additional permission to convey the resulting work.
```
