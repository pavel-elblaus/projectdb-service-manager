//go:build windows

package main

import (
	_ "embed"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

//go:embed installer.ps1
var installerPS []byte

//go:embed install-worker.ps1
var workerPS []byte

//go:embed projectdb-service-manager.cs
var managerSource []byte

//go:embed projectdb-service-control.cs
var serviceControlSource []byte

//go:embed projectdb-log-wrapper.cs
var logWrapperSource []byte

//go:embed payload/projectdb-v3.4.0-win-x64.zip
var projectDbArchive []byte

//go:embed payload/WinSW-x64.exe
var winSwBinary []byte

//go:embed payload/projectdb.ico
var projectDbIcon []byte

//go:embed THIRD-PARTY-NOTICES.txt
var thirdPartyNotices []byte

var (
	shell32           = syscall.NewLazyDLL("shell32.dll")
	user32            = syscall.NewLazyDLL("user32.dll")
	kernel32          = syscall.NewLazyDLL("kernel32.dll")
	procIsUserAnAdmin = shell32.NewProc("IsUserAnAdmin")
	procShellExecuteW = shell32.NewProc("ShellExecuteW")
	procMessageBoxW   = user32.NewProc("MessageBoxW")
	procCreateMutexW  = kernel32.NewProc("CreateMutexW")
	procCloseHandle   = kernel32.NewProc("CloseHandle")
)

const (
	setupVersion       = "0.1.0-dev"
	setupTitle         = "ProjectDB Setup " + setupVersion
	swNormal           = 1
	mbOK               = 0x00000000
	mbIconError        = 0x00000010
	mbIconInfo         = 0x00000040
	createNoWindow     = 0x08000000
	errorAlreadyExists = 183
)

func ptr(s string) *uint16 {
	p, _ := syscall.UTF16PtrFromString(s)
	return p
}

func messageBox(title, text string, flags uintptr) {
	procMessageBoxW.Call(0, uintptr(unsafe.Pointer(ptr(text))), uintptr(unsafe.Pointer(ptr(title))), flags)
}

func launcherLog(text string) {
	path := filepath.Join(os.TempDir(), "ProjectDB-Setup-launcher.log")
	line := time.Now().Format("2006-01-02 15:04:05") + " " + text + "\r\n"
	f, err := os.OpenFile(path, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0600)
	if err != nil {
		return
	}
	defer f.Close()
	_, _ = f.WriteString(line)
}

func acquireMutex() (uintptr, bool) {
	name := ptr("Local\\ProjectDBSetupInstaller")
	h, _, callErr := procCreateMutexW.Call(0, 0, uintptr(unsafe.Pointer(name)))
	if h == 0 {
		launcherLog("CreateMutexW failed: " + callErr.Error())
		return 0, false
	}
	if errno, ok := callErr.(syscall.Errno); ok && errno == errorAlreadyExists {
		procCloseHandle.Call(h)
		return 0, false
	}
	return h, true
}

func isAdmin() bool {
	r, _, _ := procIsUserAnAdmin.Call()
	return r != 0
}

func elevate() bool {
	exe, err := os.Executable()
	if err != nil {
		messageBox(setupTitle, "Could not determine setup.exe path: "+err.Error(), mbOK|mbIconError)
		return false
	}
	params := strings.Join(os.Args[1:], " ")
	r, _, _ := procShellExecuteW.Call(
		0,
		uintptr(unsafe.Pointer(ptr("runas"))),
		uintptr(unsafe.Pointer(ptr(exe))),
		uintptr(unsafe.Pointer(ptr(params))),
		0,
		swNormal,
	)
	if r <= 32 {
		messageBox(setupTitle, "Administrator privileges are required for installation.", mbOK|mbIconError)
		return false
	}
	return true
}

func main() {
	launcherLog(setupTitle + " started")

	if !isAdmin() {
		launcherLog("Requesting administrator privileges")
		elevate()
		return
	}

	mutex, ok := acquireMutex()
	if !ok {
		messageBox(setupTitle, "ProjectDB Setup is already running. Check the taskbar or Task Manager.", mbOK|mbIconInfo)
		return
	}
	defer procCloseHandle.Call(mutex)
	launcherLog("Administrator privileges confirmed")

	tmp, err := os.MkdirTemp("", "ProjectDB-Setup-")
	if err != nil {
		messageBox(setupTitle, "Could not create temporary directory: "+err.Error(), mbOK|mbIconError)
		return
	}
	defer os.RemoveAll(tmp)
	launcherLog("Temporary directory: " + tmp)

	psPath := filepath.Join(tmp, "installer.ps1")
	workerPath := filepath.Join(tmp, "install-worker.ps1")
	managerSourcePath := filepath.Join(tmp, "projectdb-service-manager.cs")
	controlPath := filepath.Join(tmp, "projectdb-service-control.cs")
	logWrapperPath := filepath.Join(tmp, "projectdb-log-wrapper.cs")
	projectDbArchivePath := filepath.Join(tmp, "projectdb-v3.4.0-win-x64.zip")
	winSwPath := filepath.Join(tmp, "WinSW-x64.exe")
	iconPath := filepath.Join(tmp, "projectdb.ico")
	thirdPartyNoticesPath := filepath.Join(tmp, "THIRD-PARTY-NOTICES.txt")

	files := []struct {
		path string
		data []byte
		name string
	}{
		{psPath, installerPS, "installer script"},
		{workerPath, workerPS, "worker script"},
		{managerSourcePath, managerSource, "ProjectDB Service Manager source"},
		{controlPath, serviceControlSource, "service control helper source"},
		{logWrapperPath, logWrapperSource, "ProjectDB log wrapper source"},
		{projectDbArchivePath, projectDbArchive, "embedded ProjectDB archive"},
		{winSwPath, winSwBinary, "embedded WinSW binary"},
		{iconPath, projectDbIcon, "ProjectDB icon"},
		{thirdPartyNoticesPath, thirdPartyNotices, "third-party license notices"},
	}
	for _, file := range files {
		if err := os.WriteFile(file.path, file.data, 0600); err != nil {
			messageBox(setupTitle, "Could not prepare "+file.name+": "+err.Error(), mbOK|mbIconError)
			return
		}
	}

	args := []string{
		"-NoProfile",
		"-ExecutionPolicy", "Bypass",
		"-STA",
		"-File", psPath,
		"-WorkerScript", workerPath,
		"-ManagerSource", managerSourcePath,
		"-ServiceControlSource", controlPath,
		"-LogWrapperSource", logWrapperPath,
		"-ProjectDbArchive", projectDbArchivePath,
		"-WinSwBinary", winSwPath,
		"-ProjectDbIcon", iconPath,
		"-ThirdPartyNotices", thirdPartyNoticesPath,
	}
	cmd := exec.Command("powershell.exe", args...)
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: createNoWindow}
	launcherLog("Starting Windows PowerShell installer UI")
	out, err := cmd.CombinedOutput()
	if err != nil {
		text := strings.TrimSpace(string(out))
		if len(text) > 3500 {
			text = text[len(text)-3500:]
		}
		if text == "" {
			text = err.Error()
		}
		launcherLog("PowerShell installer UI failed: " + text)
		messageBox(setupTitle, fmt.Sprintf("Installer failed:\r\n\r\n%s\r\n\r\nLauncher log: %s", text, filepath.Join(os.TempDir(), "ProjectDB-Setup-launcher.log")), mbOK|mbIconError)
		return
	}
	launcherLog("PowerShell installer UI exited normally")
}
