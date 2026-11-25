#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <wchar.h>

#ifndef SPI_SETDESKWALLPAPER
#define SPI_SETDESKWALLPAPER 0x0014
#endif
#ifndef SPIF_SENDCHANGE
#define SPIF_SENDCHANGE 0x0002
#endif

static void blog(const char *msg) {
    wchar_t profile[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"USERPROFILE", profile, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    wchar_t logPath[MAX_PATH * 2];
    _snwprintf(logPath, MAX_PATH * 2, L"%s\\LiveBackground\\logs\\app.log", profile);
    logPath[MAX_PATH * 2 - 1] = 0;

    SYSTEMTIME st;
    GetLocalTime(&st);
    FILE *f = _wfopen(logPath, L"a");
    if (!f) return;
    fprintf(f, "%04u-%02u-%02u %02u:%02u:%02u.%03u [INFO] bootstrapper: %s\n",
            (unsigned)st.wYear, (unsigned)st.wMonth, (unsigned)st.wDay,
            (unsigned)st.wHour, (unsigned)st.wMinute, (unsigned)st.wSecond,
            (unsigned)st.wMilliseconds, msg ? msg : "");
    fclose(f);
}

static int file_exists(const wchar_t *path) {
    DWORD a = GetFileAttributesW(path);
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

static void join_path(wchar_t *out, size_t cap, const wchar_t *a, const wchar_t *b) {
    _snwprintf(out, cap, L"%s\\%s", a, b);
    out[cap - 1] = 0;
}

static void trim_ws(wchar_t *s) {
    size_t n;
    if (!s) return;
    n = wcslen(s);
    while (n > 0 && (s[n - 1] == L'\r' || s[n - 1] == L'\n' ||
                     s[n - 1] == L' ' || s[n - 1] == L'\t')) {
        s[--n] = 0;
    }
}

static void data_path(wchar_t *out, size_t cap, const wchar_t *leaf) {
    wchar_t profile[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"USERPROFILE", profile, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) { out[0] = 0; return; }
    wchar_t root[MAX_PATH * 2];
    join_path(root, MAX_PATH * 2, profile, L"LiveBackground");
    if (leaf && leaf[0]) join_path(out, cap, root, leaf);
    else wcsncpy(out, root, cap);
    out[cap - 1] = 0;
}

static int find_manifest_frame(const wchar_t *cache, wchar_t *out, size_t cap) {
    wchar_t manifest[MAX_PATH * 2];
    join_path(manifest, MAX_PATH * 2, cache, L"manifest.json");
    HANDLE h = CreateFileW(manifest, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    char buf[8192];
    DWORD read = 0;
    if (!ReadFile(h, buf, sizeof(buf) - 1, &read, NULL) || read == 0) { CloseHandle(h); return 0; }
    CloseHandle(h);
    buf[read] = 0;
    const char *key = "\"frameFile\"";
    char *p = strstr(buf, key);
    if (!p) return 0;
    p = strchr(p + strlen(key), ':');
    if (!p) return 0;
    p++;
    while (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n') p++;
    if (*p != '"') return 0;
    p++;
    char name[MAX_PATH];
    size_t i = 0;
    while (*p && *p != '"' && i + 1 < sizeof(name)) {
        if (*p == '\\' && p[1]) { p++; }
        name[i++] = *p++;
    }
    name[i] = 0;
    if (i == 0) return 0;
    wchar_t wname[MAX_PATH];
    int n = MultiByteToWideChar(CP_UTF8, 0, name, -1, wname, MAX_PATH);
    if (n <= 0) return 0;
    join_path(out, cap, cache, wname);
    return file_exists(out);
}

static int find_frame(wchar_t *out, size_t cap) {
    wchar_t cache[MAX_PATH * 2];
    data_path(cache, MAX_PATH * 2, L"cache");
    if (!cache[0]) return 0;

    wchar_t candidate[MAX_PATH * 2];
    join_path(candidate, MAX_PATH * 2, cache, L"frame.jpg");
    if (file_exists(candidate)) { wcsncpy(out, candidate, cap); out[cap-1]=0; return 1; }

    if (find_manifest_frame(cache, out, cap)) return 1;

    wchar_t pattern[MAX_PATH * 2];
    join_path(pattern, MAX_PATH * 2, cache, L"frame-*.jpg");
    WIN32_FIND_DATAW fd;
    HANDLE fh = FindFirstFileW(pattern, &fd);
    if (fh == INVALID_HANDLE_VALUE) return 0;
    FILETIME best = fd.ftLastWriteTime;
    wchar_t bestName[MAX_PATH];
    wcsncpy(bestName, fd.cFileName, MAX_PATH); bestName[MAX_PATH-1]=0;
    while (FindNextFileW(fh, &fd)) {
        if (CompareFileTime(&fd.ftLastWriteTime, &best) > 0) {
            best = fd.ftLastWriteTime;
            wcsncpy(bestName, fd.cFileName, MAX_PATH); bestName[MAX_PATH-1]=0;
        }
    }
    FindClose(fh);
    join_path(out, cap, cache, bestName);
    return file_exists(out);
}

// Last-known-good video loop, written by the managed app on every successful
// startup. Bootstrapper only uses it when the hint says "video".
static int read_video_hint(wchar_t *media, size_t cap) {
    wchar_t path[MAX_PATH * 2];
    data_path(path, MAX_PATH * 2, L"startup-hint.txt");
    if (!path[0]) return 0;
    FILE *f = _wfopen(path, L"r, ccs=UTF-8");
    if (!f) return 0;
    wchar_t mode[64];
    wchar_t value[MAX_PATH * 2];
    int ok = 0;
    if (fgetws(mode, 64, f) && fgetws(value, MAX_PATH * 2, f)) {
        trim_ws(mode);
        trim_ws(value);
        if (_wcsicmp(mode, L"video") == 0 && value[0] && file_exists(value)) {
            wcsncpy(media, value, cap);
            media[cap - 1] = 0;
            ok = 1;
        }
    }
    fclose(f);
    return ok;
}

static void handoff_path(wchar_t *out, size_t cap, const wchar_t *leaf) {
    wchar_t cache[MAX_PATH * 2];
    data_path(cache, MAX_PATH * 2, L"cache");
    if (!cache[0]) { out[0] = 0; return; }
    CreateDirectoryW(cache, NULL);
    join_path(out, cap, cache, leaf);
}

// Write to a temp file then replace, so the managed reader never sees a
// half-written handoff.
static void write_handoff(const wchar_t *first, const wchar_t *media) {
    wchar_t finalPath[MAX_PATH * 2];
    wchar_t tempPath[MAX_PATH * 2];
    handoff_path(finalPath, MAX_PATH * 2, L"early-mpv.txt");
    handoff_path(tempPath, MAX_PATH * 2, L"early-mpv.tmp");
    if (!finalPath[0] || !tempPath[0]) return;
    FILE *f = _wfopen(tempPath, L"w, ccs=UTF-8");
    if (!f) return;
    fwprintf(f, L"%s\n%s\n", first, media);
    fclose(f);
    MoveFileExW(tempPath, finalPath, MOVEFILE_REPLACE_EXISTING);
}

static void clear_handoff(void) {
    wchar_t path[MAX_PATH * 2];
    handoff_path(path, MAX_PATH * 2, L"early-mpv.txt");
    if (path[0]) DeleteFileW(path);
}

static int app_already_running(void) {
    HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, L"Local\\LiveBackgroundApp");
    if (!mutex) return 0;
    CloseHandle(mutex);
    return 1;
}

static int mpv_already_running(void) {
    int found = 0;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W entry;
    ZeroMemory(&entry, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snap, &entry)) {
        do {
            if (_wcsicmp(entry.szExeFile, L"mpv.exe") == 0) { found = 1; break; }
        } while (Process32NextW(snap, &entry));
    }
    CloseHandle(snap);
    return found;
}

static HWND wait_for_progman(DWORD timeoutMs) {
    DWORD start = GetTickCount();
    for (;;) {
        HWND window = FindWindowW(L"Progman", NULL);
        if (window) return window;
        if (GetTickCount() - start >= timeoutMs) return NULL;
        Sleep(50);
    }
}

static int start_early_mpv(HWND progman, const wchar_t *media, const wchar_t *programRoot, DWORD *pidOut) {
    wchar_t mpv[MAX_PATH * 2];
    join_path(mpv, MAX_PATH * 2, programRoot, L"mpv\\mpv.exe");
    if (!file_exists(mpv)) return 0;

    wchar_t shaders[MAX_PATH * 2];
    data_path(shaders, MAX_PATH * 2, L"cache\\mpv-shaders");
    if (!shaders[0]) return 0;
    CreateDirectoryW(shaders, NULL);

    wchar_t cmd[32768];
    _snwprintf(cmd, 32768,
        L"\"%s\" --wid=%lld --loop-file=inf --no-config --load-scripts=no "
        L"--media-controls=no --no-audio --no-osc --no-osd-bar "
        L"--no-input-default-bindings --no-border --gpu-api=d3d11 --hwdec=auto "
        L"--panscan=1.0 --force-window=yes --terminal=no --no-resume-playback "
        L"--sub-auto=no --audio-file-auto=no --stop-screensaver=yes --cache=no "
        L"--demuxer-lavf-analyzeduration=0 "
        L"--demuxer-lavf-probesize=32768 --gpu-shader-cache=yes "
        L"--gpu-shader-cache-dir=\"%s\" \"%s\"",
        mpv, (long long)(INT_PTR)progman, shaders, media);
    cmd[32767] = 0;

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));
    if (!CreateProcessW(mpv, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, programRoot, &si, &pi))
        return 0;
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);

    wchar_t pidText[32];
    _snwprintf(pidText, 32, L"%lu", (unsigned long)pi.dwProcessId);
    pidText[31] = 0;
    write_handoff(pidText, media);
    if (pidOut) *pidOut = pi.dwProcessId;
    return 1;
}

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE hPrev, PWSTR lpCmdLine, int nShow) {
    (void)hInst; (void)hPrev; (void)nShow;
    blog("started");

    wchar_t frame[MAX_PATH * 2];
    if (find_frame(frame, MAX_PATH * 2)) {
        // Best-effort: show the cached frame immediately. Main app will take over.
        SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, frame, SPIF_SENDCHANGE);
        blog("cached frame applied");
    } else {
        blog("no cached frame found");
    }

    wchar_t exePath[MAX_PATH * 2];
    DWORD len = GetModuleFileNameW(NULL, exePath, MAX_PATH * 2);
    if (len == 0 || len >= MAX_PATH * 2) { blog("GetModuleFileName failed"); return 1; }
    wchar_t *slash = wcsrchr(exePath, L'\\');
    if (!slash) { blog("bad module path"); return 1; }
    *(slash + 1) = 0;
    wchar_t mainExe[MAX_PATH * 2];
    _snwprintf(mainExe, MAX_PATH * 2, L"%sLiveBackground.exe", exePath);
    mainExe[MAX_PATH * 2 - 1] = 0;

    // Forward command line args (e.g. --startup, --watchdog) to the main app.
    wchar_t cmd[32768];
    const wchar_t *args = lpCmdLine ? lpCmdLine : L"";
    while (*args == L' ') args++;
    if (*args) _snwprintf(cmd, 32768, L"\"%s\" %s", mainExe, args);
    else _snwprintf(cmd, 32768, L"\"%s\"", mainExe);
    cmd[32767] = 0;

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    // Only a true logon startup may hand mpv off early. The 5-minute watchdog
    // and manual launches must never start a second video.
    int wantEarlyMpv = (wcsstr(args, L"--startup") != NULL);
    HANDLE earlyLock = NULL;
    wchar_t earlyMedia[MAX_PATH * 2];
    earlyMedia[0] = 0;
    if (wantEarlyMpv) {
        clear_handoff();
        if (app_already_running()) {
            blog("early mpv skipped: app already running");
            wantEarlyMpv = 0;
        } else {
            SetLastError(0);
            earlyLock = CreateMutexW(NULL, FALSE, L"Local\\LiveBackgroundEarlyMpv");
            if (GetLastError() == ERROR_ALREADY_EXISTS) {
                blog("early mpv skipped: another bootstrapper owns the handoff");
                CloseHandle(earlyLock);
                earlyLock = NULL;
                wantEarlyMpv = 0;
            } else if (mpv_already_running()) {
                blog("early mpv skipped: mpv already running");
                wantEarlyMpv = 0;
            } else if (!read_video_hint(earlyMedia, MAX_PATH * 2)) {
                blog("early mpv skipped: no video hint");
                wantEarlyMpv = 0;
            } else {
                // Tell the managed app to wait for us before it starts its own.
                write_handoff(L"pending", earlyMedia);
            }
        }
    }

    int mainLaunched = 0;
    if (CreateProcessW(mainExe, cmd, NULL, NULL, FALSE, 0, NULL, exePath, &si, &pi)) {
        CloseHandle(pi.hThread);
        mainLaunched = 1;
        blog("main exe launched");
    } else {
        blog("CreateProcess for main exe failed");
    }

    // If the main exe exited immediately (another instance won the mutex), do
    // not leave an mpv behind with no owner.
    if (mainLaunched && WaitForSingleObject(pi.hProcess, 0) == WAIT_OBJECT_0) {
        blog("early mpv skipped: main exe exited");
        wantEarlyMpv = 0;
        clear_handoff();
    }

    if (wantEarlyMpv && mainLaunched) {
        HWND progman = wait_for_progman(20000);
        if (progman) {
            DWORD pid = 0;
            if (start_early_mpv(progman, earlyMedia, exePath, &pid)) {
                char message[128];
                _snprintf(message, sizeof(message), "early mpv started pid %lu", (unsigned long)pid);
                message[sizeof(message) - 1] = 0;
                blog(message);
            } else {
                blog("early mpv start failed");
                clear_handoff();
            }
        } else {
            blog("early mpv skipped: Progman not ready");
            clear_handoff();
        }
    }
    if (pi.hProcess) CloseHandle(pi.hProcess);
    if (earlyLock) CloseHandle(earlyLock);
    return 0;
}
