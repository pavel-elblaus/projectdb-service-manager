Offline payload for ProjectDB Setup.

The large payload binaries are not stored in this Git repository.
build-offline.ps1 downloads the pinned files when they are missing and verifies SHA-256 before compilation.
The generated ProjectDB Setup EXE embeds both dependencies and remains fully offline at installation time.

projectdb-v3.4.0-win-x64.zip
Source: https://github.com/pavel-elblaus/projectdb/releases/download/17.8.0/projectdb-v3.4.0-win-x64.zip
SHA-256: 3878c4eba1337e040aea30b9428b07ba6f6a062d7c518db489bcc47f85213758

WinSW-x64.exe 2.12.0
Source: https://github.com/winsw/winsw/releases/download/v2.12.0/WinSW-x64.exe
SHA-256: 05b82d46ad331cc16bdc00de5c6332c1ef818df8ceefcd49c726553209b3a0da
License: MIT (see ..\THIRD-PARTY-NOTICES.txt)

projectdb.ico
ProjectDB icon used by Setup and installed management components.
