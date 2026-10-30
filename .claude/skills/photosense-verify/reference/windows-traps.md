# Windows traps

- **Git Bash** rewrites MSBuild switches written with a slash (`/p:`, `/warnaserror`) into paths. Write them with a dash (`-p:`, `-warnaserror`).
- **Idle MSBuild workers** keep DLLs locked and break the next build (MSB3027). Always build with `-nodeReuse:false`.
- **Heredocs:** a heredoc with quotes or backslashes often breaks in the Bash tool, and backslashes inside a Python string in one get mangled. Write a script to the session scratchpad and run it with `python -I <path>`, or use the Edit tool for an exact change. Above all, use the Edit tool for anything with `\` in it.
- **`rm -rf` follows junctions.** Never link a scratch copy to the repo's `node_modules`. Remove a junction only with `[IO.Directory]::Delete(path)`.
- **"It's locked, so it will refuse"** is no safeguard for a folder: a recursive delete removes everything it can first.
- **robocopy's exit codes 1 to 7 mean success.** Only 8 and above are failures.
- **PowerShell:** `Select-String` patterns are regular expressions. A path of one's own goes in quotes when it has spaces (`"C:\Program Files (x86)\…"`).
- **The `.NET` SDK** picked is the latest feature band installed (`global.json` rolls forward). The workloads Visual Studio installed are for the 10.0.100 band, and a workload pack missing from Program Files can be given from `DOTNETSDK_WORKLOAD_PACK_ROOTS` (see `photosense-phone-apps`).
