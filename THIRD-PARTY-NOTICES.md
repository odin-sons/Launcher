# Third-Party Notices

This project includes the following third-party component.

## SteamQuery.dll

Used by `Launcher` (the GUI) to query a server's live player count as a fallback when
the HTTP status endpoint is unavailable — see `MainWindow.xaml.cs`, `UpdateServerStatusAsync`.
Referenced as a loose binary via `<Reference HintPath="SteamQuery.dll">` in
`Valheim-Online_Launcher.csproj` (see the note on packaging below).

**Provenance — best effort, not fully verified.** The compiled DLL embeds a PDB path
(`...\RiderProjects\SteamQuery-main\SteamQuery\obj\x64\Debug\net6.0\SteamQuery.pdb`)
indicating it was built from a GitHub repository named exactly `SteamQuery` (default
branch `main`), but no such repository could be located publicly. Its type surface —
`ServerInfoQuery`, `ServerPlayersQuery`, `ServerRulesQuery`, `EndPointAddress`,
generic `ServerQuery<T>` with ping measurement — closely matches the SteamQueryNet
family (`cyilcode/SteamQueryNet` → `razaqq/SteamQueryNet` → `Tyler-OBrien/SteamQueryNet`,
published to NuGet as package `SteamQuery` 1.1.0, deprecated), all MIT-licensed and
descended from Cem YILMAZ's 2018 original, but does not match any of those exactly
(richer API, different assembly metadata). The notice below is carried forward from
that lineage as the best available attribution; it has not been confirmed against the
exact source of this specific binary.

```
MIT License

    Copyright for original code of SteamQueryNet are held by Cem YILMAZ, 2018 and
    contributors. All other copyright for SteamQueryNet are held by Tyler O'Brien, 2022
    under the same license.

Copyright (c) 2018 Cem YILMAZ

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
