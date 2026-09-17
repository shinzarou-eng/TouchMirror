// TouchMirror.AirPlayHost — thin native receiver host.
//
// Loads airplay2dll.dll, starts the AirPlay/RAOP servers, and forwards
// decoded video frames plus session events to TouchMirror over named pipes.
// Display-only: no input path exists here by design.
//
// Build: see tools/build-airplay-host.ps1

#include <windows.h>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <string>

#include "Airplay2Head.h"

namespace {

constexpr uint32_t kMsgVideo = 1;
constexpr uint32_t kMsgAudio = 2;

struct Pipe {
    HANDLE h = INVALID_HANDLE_VALUE;

    bool connect(const char* name, DWORD timeoutMs) {
        std::string path = "\\\\.\\pipe\\";
        path += name;
        const DWORD deadline = GetTickCount() + timeoutMs;
        for (;;) {
            h = CreateFileA(path.c_str(), GENERIC_WRITE, 0, nullptr,
                            OPEN_EXISTING, 0, nullptr);
            if (h != INVALID_HANDLE_VALUE)
                return true;
            if (GetLastError() != ERROR_PIPE_BUSY || GetTickCount() >= deadline)
                return false;
            if (!WaitNamedPipeA(path.c_str(), 500) && GetTickCount() >= deadline)
                return false;
        }
    }

    bool write(const void* data, DWORD len) {
        DWORD written = 0;
        return WriteFile(h, data, len, &written, nullptr) && written == len;
    }

    void close() {
        if (h != INVALID_HANDLE_VALUE) {
            CloseHandle(h);
            h = INVALID_HANDLE_VALUE;
        }
    }
};

Pipe g_videoPipe;
Pipe g_eventPipe;
CRITICAL_SECTION g_videoWriteLock; // un message = en-tête + payload atomiques
volatile bool g_running = true;

void emitEvent(const char* type, const char* name, const char* deviceId) {
    char line[640];
    // Names/ids come from the network peer; cap them defensively.
    char safeName[160];
    char safeId[160];
    snprintf(safeName, sizeof(safeName), "%.150s", name ? name : "");
    snprintf(safeId, sizeof(safeId), "%.150s", deviceId ? deviceId : "");
    for (char* p = safeName; *p; ++p) if (*p == '"' || *p == '\\') *p = '_';
    for (char* p = safeId; *p; ++p) if (*p == '"' || *p == '\\') *p = '_';
    const int n = snprintf(line, sizeof(line),
        "{\"type\":\"%s\",\"name\":\"%s\",\"deviceId\":\"%s\"}\n",
        type, safeName, safeId);
    if (n > 0)
        g_eventPipe.write(line, (DWORD)n);
}

class HostCallback final : public IAirServerCallback {
public:
    void connected(const char* remoteName, const char* remoteDeviceId) override {
        emitEvent("connected", remoteName, remoteDeviceId);
    }

    void disconnected(const char* remoteName, const char* remoteDeviceId) override {
        emitEvent("disconnected", remoteName, remoteDeviceId);
    }

    void outputAudio(SFgAudioFrame* data, const char*, const char*) override {
        if (!data || !data->data || data->dataLen == 0)
            return;
        const uint32_t payload =
            4 + 8 + 4 + 2 + 2 + 4 + data->dataLen;
        uint8_t head[4 + 4 + 8 + 4 + 2 + 2 + 4];
        uint8_t* p = head;
        auto put32 = [&](uint32_t v) { memcpy(p, &v, 4); p += 4; };
        auto put64 = [&](uint64_t v) { memcpy(p, &v, 8); p += 8; };
        auto put16 = [&](uint16_t v) { memcpy(p, &v, 2); p += 2; };
        put32(payload);
        put32(kMsgAudio);
        put64(data->pts);
        put32(data->sampleRate);
        put16(data->channels);
        put16(data->bitsPerSample);
        put32(data->dataLen);
        EnterCriticalSection(&g_videoWriteLock);
        if (!g_videoPipe.write(head, sizeof(head))
            || !g_videoPipe.write(data->data, data->dataLen))
            g_running = false;
        LeaveCriticalSection(&g_videoWriteLock);
    }

    void outputVideo(SFgVideoFrame* data, const char* remoteName,
                     const char* remoteDeviceId) override {
        if (!data || !data->data || data->dataTotalLen == 0)
            return;
        const char* id = remoteDeviceId ? remoteDeviceId : "";
        const uint32_t idLen = (uint32_t)strlen(id);
        const uint32_t payload =
            4 + 8 + 4 + 4 + 12 + 12 + 1 + 4 + idLen + data->dataTotalLen;
        uint8_t head[4 + 4 + 8 + 4 + 4 + 12 + 12 + 1 + 4];
        uint8_t* p = head;
        auto put32 = [&](uint32_t v) { memcpy(p, &v, 4); p += 4; };
        auto put64 = [&](uint64_t v) { memcpy(p, &v, 8); p += 8; };
        put32(payload);
        put32(kMsgVideo);
        put64(data->pts);
        put32(data->width);
        put32(data->height);
        put32(data->pitch[0]);
        put32(data->pitch[1]);
        put32(data->pitch[2]);
        put32(data->dataLen[0]);
        put32(data->dataLen[1]);
        put32(data->dataLen[2]);
        *p++ = data->isKey ? 1 : 0;
        put32(idLen);
        EnterCriticalSection(&g_videoWriteLock);
        if (!g_videoPipe.write(head, sizeof(head))
            || (idLen > 0 && !g_videoPipe.write(id, idLen))
            || !g_videoPipe.write(data->data, data->dataTotalLen))
            g_running = false;
        LeaveCriticalSection(&g_videoWriteLock);
    }

    void videoPlay(char*, double, double) override {}

    void videoGetPlayInfo(double* duration, double* position,
                          double* rate) override {
        if (duration) *duration = 0.0;
        if (position) *position = 0.0;
        if (rate) *rate = 0.0;
    }

    void setVolume(float, const char*, const char*) override {}

    void log(int level, const char* msg) override {
        fprintf(stderr, "[airplay:%d] %s\n", level, msg ? msg : "");
    }
};

const char* argValue(int argc, char** argv, const char* key,
                     const char* fallback) {
    for (int i = 1; i + 1 < argc; ++i)
        if (strcmp(argv[i], key) == 0)
            return argv[i + 1];
    return fallback;
}

BOOL WINAPI onConsoleSignal(DWORD type) {
    if (type == CTRL_C_EVENT || type == CTRL_CLOSE_EVENT ||
        type == CTRL_BREAK_EVENT) {
        g_running = false;
        return TRUE;
    }
    return FALSE;
}

} // namespace

int main(int argc, char** argv) {
    const char* name = argValue(argc, argv, "--name", "TouchMirror");
    const int raopPort = atoi(argValue(argc, argv, "--raop-port", "5001"));
    const int airplayPort = atoi(argValue(argc, argv, "--airplay-port", "7001"));
    const char* videoPipe = argValue(argc, argv, "--video-pipe", nullptr);
    const char* eventPipe = argValue(argc, argv, "--event-pipe", nullptr);

    if (!videoPipe || !eventPipe) {
        fprintf(stderr, "usage: AirPlayHost --video-pipe P --event-pipe P "
                        "[--name N] [--raop-port N] [--airplay-port N]\n");
        return 2;
    }

    InitializeCriticalSection(&g_videoWriteLock);

    if (!g_videoPipe.connect(videoPipe, 15000)) {
        fprintf(stderr, "video pipe connect failed\n");
        return 3;
    }
    if (!g_eventPipe.connect(eventPipe, 15000)) {
        fprintf(stderr, "event pipe connect failed\n");
        return 3;
    }

    SetConsoleCtrlHandler(onConsoleSignal, TRUE);

    static HostCallback callback;
    char serverName[AIRPLAY_NAME_LEN];
    snprintf(serverName, sizeof(serverName), "%.120s", name);

    void* handle = fgServerStart(serverName, (unsigned)raopPort,
                                 (unsigned)airplayPort, &callback);
    if (!handle) {
        fprintf(stderr, "fgServerStart failed\n");
        return 4;
    }

    emitEvent("ready", name, "");
    fprintf(stderr, "AirPlay host up: %s raop=%d airplay=%d\n",
            serverName, raopPort, airplayPort);

    while (g_running)
        Sleep(200);

    fgServerStop(handle);
    g_videoPipe.close();
    g_eventPipe.close();
    return 0;
}
