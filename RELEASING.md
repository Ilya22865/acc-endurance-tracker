# AccEndurance releases

## GitHub repository setup

1. Push this project to a **public** GitHub repository. Public is recommended because the updater uses anonymous GitHub Releases access and no token is embedded in the app.
2. In `src/AccEndurance.Client/update-source.json`, replace `OWNER/AccEndurance` with the real repository URL for local builds. GitHub Actions writes the correct URL automatically for release builds.
3. Ensure GitHub Actions is enabled and the workflow permission `Contents: Read and write` is allowed (the workflow also requests `contents: write`).

## Publish a release

Create and push a semantic version tag, for example:

```bash
git tag v0.1.0
git push origin v0.1.0
```

GitHub Actions builds the Windows x64, Linux x64, and macOS ARM64 packages and attaches their Velopack assets to the matching GitHub Release. Each platform uses a separate update channel (`win`, `linux`, `osx`) and package ID to prevent cross-platform packages from being installed on the wrong OS.

For the next release, increase the version and push the next tag (for example `v0.1.1`). Users with a Velopack-installed copy will check GitHub Releases on startup, download the update in the background, apply it, and restart.

## First installation

Download the matching first-install artifact from the GitHub Release:

- Windows: `AccEndurance-win-Setup.exe`
- macOS: `AccEndurance-osx.pkg`
- Linux: `AccEndurance-linux.AppImage` (mark it executable if your file manager does not do so automatically)

Linux does not have a traditional setup wizard in Velopack; its AppImage is self-updating. macOS package installation and subsequent updates require code signing and notarization for a smooth Gatekeeper experience. Windows signing is strongly recommended to reduce SmartScreen warnings.

## Important notes

- Automatic updates work only for installations made with the Velopack packages. Running from Visual Studio or launching a manually copied publish folder does not self-update.
- Keep the GitHub repository public for this anonymous-client setup. GitHub anonymous API access is limited to 60 requests/hour per IP; for a large audience sharing a NAT, consider a dedicated update host or a secure proxy. Do not embed a personal access token in the client.
- `server.url` is migrated from beside the executable into the user's per-OS application-data folder. Keep user data and databases outside the Velopack application directory.
- This workflow packages the **desktop client** only. The ASP.NET server, database, Agent, and Broadcaster have their own deployment lifecycle and are not updated by the desktop updater.
- macOS packages must be built on a macOS runner. The current workflow targets Apple Silicon (`osx-arm64`); add a separate Intel (`osx-x64`) build if Intel Macs must be supported.
