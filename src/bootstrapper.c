#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#include <wchar.h>

#ifndef SPI_SETDESKWALLPAPER
#define SPI_SETDESKWALLPAPER 0x0014
#endif
#ifndef SPIF_SENDCHANGE
#define SPIF_SENDCHANGE 0x0002
#endif

static int file_exists(const wchar_t *path) {
    DWORD a = GetFileAttributesW(path);
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

static void join_path(wchar_t *out, size_t cap, const wchar_t *a, const wchar_t *b) {
    _snwprintf(out, cap, L"%s\\%s", a, b);
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
    wchar_t profile[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"USERPROFILE", profile, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return 0;
    wchar_t cache[MAX_PATH * 2];
    _snwprintf(cache, MAX_PATH * 2, L"%s\\LiveBackground\\cache", profile);
    cache[MAX_PATH * 2 - 1] = 0;

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

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE hPrev, PWSTR lpCmdLine, int nShow) {
    (void)hInst; (void)hPrev; (void)lpCmdLine; (void)nShow;

    wchar_t frame[MAX_PATH * 2];
    if (find_frame(frame, MAX_PATH * 2)) {
        // Best-effort: show the cached frame immediately. Main app will take over.
        SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, frame, SPIF_SENDCHANGE);
    }

    wchar_t exePath[MAX_PATH * 2];
    DWORD len = GetModuleFileNameW(NULL, exePath, MAX_PATH * 2);
    if (len == 0 || len >= MAX_PATH * 2) return 1;
    wchar_t *slash = wcsrchr(exePath, L'\\');
    if (!slash) return 1;
    *(slash + 1) = 0;
    wchar_t mainExe[MAX_PATH * 2];
    _snwprintf(mainExe, MAX_PATH * 2, L"%sLiveBackground.exe", exePath);
    mainExe[MAX_PATH * 2 - 1] = 0;

    // Forward command line args (e.g. --startup, --watchdog) to the main app.
    wchar_t cmd[32768];
    const wchar_t *args = lpCmdLine;
    while (*args == L' ') args++;
    if (*args) _snwprintf(cmd, 32768, L"\"%s\" %s", mainExe, args);
    else _snwprintf(cmd, 32768, L"\"%s\"", mainExe);
    cmd[32767] = 0;

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));
    if (CreateProcessW(mainExe, cmd, NULL, NULL, FALSE, 0, NULL, exePath, &si, &pi)) {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
    return 0;
}
