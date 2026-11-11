# EnduranceHub releases

## GitHub repository setup

1. Push this project to a **public** GitHub repository. Public is recommended because the updater uses anonymous GitHub Releases access and no token is embedded in the app.
2. In `src/EnduranceHub.Client/update-source.json`, replace `OWNER/EnduranceHub` with the real repository URL for local builds. GitHub Actions writes the correct URL automatically for release builds.
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

- Windows (wizard): `EnduranceHub-win-win.msi`
- Windows (one-click): `EnduranceHub-win-win-Setup.exe`
- macOS: `EnduranceHub-osx-osx-Setup.pkg`
- Linux: `EnduranceHub-linux.AppImage` (mark it executable if your file manager does not do so automatically)

The Windows build ships two installers:

- **MSI** (`--msi --instLocation Either`) is the recommended download. It runs the standard Windows installer wizard and has an `InstallScopeDlg` step where the user chooses **per-user** (`%LOCALAPPDATA%\EnduranceHub-win\current`, no admin rights) or **per-machine** (`C:\Program Files\EnduranceHub\current`, admin rights). The MSI registers in Apps & Features, so uninstall and repair work normally. A fixed path can also be supplied non-interactively with `msiexec /i EnduranceHub-win-win.msi VELOPACK_INSTALLDIR="D:\Apps\EnduranceHub"`.
- **Setup.exe** is Velopack's one-click installer with no wizard and no path question. Keep it as a fallback for users who want the fastest path.

Velopack's MSI UI does not expose a free-form path field (`BrowseDlg` exists in the WiX template but is not wired into the dialog flow). Supporting that would require a custom MSI transform or wrapper installer, which increases AV/SmartScreen risk and is intentionally not part of this workflow.

The Windows package bundles the desktop client together with `Server\EnduranceHub.Server.exe` and `Agent\EnduranceHub.Agent.exe`. The client always resolves those two executables from its own installation folder, so a custom install location keeps working.

User data (`settings.json`, `server.appsettings.json`, logs, autosave) lives in `%LOCALAPPDATA%\EnduranceHub` and is independent of the installation folder.

## Code signing (SmartScreen / antivirus)

Unsigned installers always show SmartScreen's "Unknown publisher" prompt, no matter how clean the build is. Windows Defender currently scans the release binaries clean; the warning is a reputation issue, and only a code-signing certificate fixes it. The workflow is ready for signing and stays unsigned when no secrets are present.

Configure one of these repository secrets:

| Secret | Purpose |
|--------|---------|
| `SIGNING_PFX_BASE64` + `SIGNING_PFX_PASSWORD` | Base64 of a `.pfx` code-signing certificate and its password. The workflow decodes it and passes `/f`, `/p`, SHA-256 digest and a DigiCert timestamp to `signtool` via `VPK_SIGN_PARAMS`. |
| `VPK_SIGN_PARAMS` | Raw `signtool` parameters, e.g. for a hardware token or a custom timestamp server. Used only when the PFX secrets are absent. |

`vpk` signs the app binaries, the portable zip, `Setup.exe` and the MSI. Free options for open-source projects: **SignPath Foundation** (free OV/EV signing for OSS) and **Azure Trusted Signing** (cheap monthly subscription). An EV certificate clears SmartScreen immediately; an OV certificate builds reputation over time.

Every release also gets a `SHA256SUMS.txt` asset generated in CI. Users can verify a download with:

```powershell
Get-FileHash .\EnduranceHub-win-win.msi -Algorithm SHA256
```

## Important notes

- Automatic updates work only for installations made with the Velopack packages. Running from Visual Studio or launching a manually copied publish folder does not self-update.
- Keep the GitHub repository public for this anonymous-client setup. GitHub anonymous API access is limited to 60 requests/hour per IP; for a large audience sharing a NAT, consider a dedicated update host or a secure proxy. Do not embed a personal access token in the client.
- `server.url` is migrated from beside the executable into the user's per-OS application-data folder. Keep user data and databases outside the Velopack application directory.
- This workflow bundles the desktop client, the ASP.NET server, and the Agent into the Windows installer. The database still has its own deployment lifecycle, and Linux/macOS packages contain the desktop client only.
- macOS packages must be built on a macOS runner. The current workflow targets Apple Silicon (`osx-arm64`); add a separate Intel (`osx-x64`) build if Intel Macs must be supported.
- Linux has no Velopack setup wizard; its AppImage is self-updating. macOS package installation and updates require signing and notarization for a smooth Gatekeeper experience.
- MSI and `Setup.exe` installs use the same Velopack package layout, so in-app updates keep working. The MSI keeps the same `UpgradeCode` across versions, which lets Windows replace an older install instead of stacking copies.
