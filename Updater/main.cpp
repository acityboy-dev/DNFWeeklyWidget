#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <winhttp.h>
#include <bcrypt.h>
#include <shellapi.h>

#include <algorithm>
#include <cstdio>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <map>
#include <memory>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

extern "C" {
#include "third_party/miniz/miniz.h"
}

namespace fs = std::filesystem;

namespace {
constexpr wchar_t kWindowClass[] = L"DNFWeeklyWidget.NativeUpdater";
constexpr wchar_t kWindowTitle[] = L"DNFWeeklyWidget 업데이트";
constexpr wchar_t kManagedFilesName[] = L".update-files.json";
constexpr UINT kStatusMessage = WM_APP + 1;
constexpr UINT kProgressMessage = WM_APP + 2;
constexpr UINT kErrorMessage = WM_APP + 3;
constexpr UINT kDoneMessage = WM_APP + 4;
constexpr int kStatusControl = 1001;
constexpr int kProgressControl = 1002;

struct Options {
    DWORD processId{};
    fs::path installDirectory;
    std::wstring packageUrl;
    std::wstring sha256;
    fs::path executable;
    std::wstring currentVersion;
    std::wstring latestVersion;
    fs::path acceptedFile;
    bool skipConfirmation{};
};

struct PendingFile {
    std::wstring relativePath;
    fs::path temporaryPath;
    fs::path destinationPath;
};

struct ExtractContext { HANDLE file{INVALID_HANDLE_VALUE}; };

HWND g_window{};
HWND g_status{};
HWND g_progress{};
HFONT g_statusFont{};
Options g_options;
bool g_allowClose{};

std::wstring GetArgument(const std::vector<std::wstring>& args, const std::wstring& name) {
    for (size_t i = 0; i + 1 < args.size(); ++i) {
        if (_wcsicmp(args[i].c_str(), name.c_str()) == 0) return args[i + 1];
    }
    return {};
}

bool HasArgument(const std::vector<std::wstring>& args, const std::wstring& name) {
    return std::any_of(args.begin(), args.end(), [&](const auto& value) { return _wcsicmp(value.c_str(), name.c_str()) == 0; });
}

std::wstring Win32Error(const std::wstring& prefix, DWORD error = GetLastError()) {
    wchar_t* message{};
    FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr, error, 0, reinterpret_cast<wchar_t*>(&message), 0, nullptr);
    std::wstring result = prefix;
    if (message) { result += L"\n"; result += message; LocalFree(message); }
    return result;
}

std::wstring Utf8ToWide(const char* value) {
    if (!value || !*value) return {};
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, nullptr, 0);
    if (size <= 0) throw std::runtime_error("Invalid UTF-8 ZIP entry name");
    std::wstring result(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, result.data(), size);
    result.pop_back();
    return result;
}

std::string WideToUtf8(const std::wstring& value) {
    const int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), size, nullptr, nullptr);
    result.pop_back();
    return result;
}

void PostStatus(const std::wstring& text) { PostMessageW(g_window, kStatusMessage, 0, reinterpret_cast<LPARAM>(new std::wstring(text))); }
void PostProgress(int value) { PostMessageW(g_window, kProgressMessage, static_cast<WPARAM>(std::clamp(value, 0, 100)), 0); }

void EnsureSafeRelativePath(const std::wstring& relativePath) {
    fs::path path(relativePath);
    if (path.empty() || path.is_absolute() || path.has_root_name()) throw std::runtime_error("Unsafe ZIP path");
    for (const auto& part : path) {
        if (part == L".." || part == L".") throw std::runtime_error("Unsafe ZIP path");
    }
}

fs::path DestinationPath(const fs::path& root, const std::wstring& relativePath) {
    EnsureSafeRelativePath(relativePath);
    const fs::path fullRoot = fs::weakly_canonical(root);
    const fs::path destination = fs::weakly_canonical(root / fs::path(relativePath));
    auto rootText = fullRoot.native();
    if (!rootText.empty() && rootText.back() != L'\\') rootText += L'\\';
    const auto destinationText = destination.native();
    if (destinationText.size() < rootText.size() || _wcsnicmp(destinationText.c_str(), rootText.c_str(), rootText.size()) != 0)
        throw std::runtime_error("Unsafe ZIP destination");
    return destination;
}

void WriteAcceptedFlag() {
    fs::create_directories(g_options.acceptedFile.parent_path());
    std::ofstream file(g_options.acceptedFile, std::ios::binary | std::ios::trunc);
    file << "accepted";
    if (!file) throw std::runtime_error("Unable to create update handshake file");
}

void WaitForProcessExit(DWORD processId) {
    HANDLE process = OpenProcess(SYNCHRONIZE, FALSE, processId);
    if (!process) {
        if (GetLastError() == ERROR_INVALID_PARAMETER) return;
        throw std::runtime_error("Unable to open application process");
    }
    const DWORD result = WaitForSingleObject(process, 60000);
    CloseHandle(process);
    if (result != WAIT_OBJECT_0) throw std::runtime_error("Application did not exit within 60 seconds");
}

void DownloadFile(const std::wstring& url, const fs::path& destination) {
    URL_COMPONENTS components{sizeof(components)};
    components.dwSchemeLength = static_cast<DWORD>(-1);
    components.dwHostNameLength = static_cast<DWORD>(-1);
    components.dwUrlPathLength = static_cast<DWORD>(-1);
    components.dwExtraInfoLength = static_cast<DWORD>(-1);
    if (!WinHttpCrackUrl(url.c_str(), 0, 0, &components)) throw std::runtime_error("Invalid package URL");

    const std::wstring host(components.lpszHostName, components.dwHostNameLength);
    std::wstring path(components.lpszUrlPath, components.dwUrlPathLength);
    if (components.dwExtraInfoLength) path.append(components.lpszExtraInfo, components.dwExtraInfoLength);

    HINTERNET session = WinHttpOpen(L"DNFWeeklyWidget-Updater/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, nullptr, nullptr, 0);
    if (!session) throw std::runtime_error("Unable to initialize WinHTTP");
    WinHttpSetTimeouts(session, 15000, 15000, 15000, 600000);
    HINTERNET connection = WinHttpConnect(session, host.c_str(), components.nPort, 0);
    HINTERNET request = connection ? WinHttpOpenRequest(connection, L"GET", path.c_str(), nullptr, WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES, components.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0) : nullptr;
    if (!connection || !request || !WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, nullptr, 0, 0, 0) || !WinHttpReceiveResponse(request, nullptr)) {
        if (request) WinHttpCloseHandle(request); if (connection) WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        throw std::runtime_error("Unable to download update package");
    }

    DWORD status{}, statusSize = sizeof(status);
    WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, nullptr, &status, &statusSize, nullptr);
    if (status < 200 || status >= 300) { WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session); throw std::runtime_error("Update server returned an error"); }

    wchar_t lengthText[64]{}; DWORD lengthSize = sizeof(lengthText);
    unsigned long long total{};
    if (WinHttpQueryHeaders(request, WINHTTP_QUERY_CONTENT_LENGTH, nullptr, lengthText, &lengthSize, nullptr)) total = _wcstoui64(lengthText, nullptr, 10);

    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) { WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session); throw std::runtime_error("Unable to create update package"); }

    std::vector<unsigned char> buffer(128 * 1024);
    unsigned long long downloaded{};
    while (true) {
        DWORD read{};
        if (!WinHttpReadData(request, buffer.data(), static_cast<DWORD>(buffer.size()), &read)) { CloseHandle(file); throw std::runtime_error("Update download failed"); }
        if (!read) break;
        DWORD written{};
        if (!WriteFile(file, buffer.data(), read, &written, nullptr) || written != read) { CloseHandle(file); throw std::runtime_error("Unable to save update package"); }
        downloaded += read;
        if (total) PostProgress(static_cast<int>(downloaded * 100 / total));
    }
    FlushFileBuffers(file); CloseHandle(file);
    WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
}

std::wstring Sha256(const fs::path& path) {
    BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) throw std::runtime_error("SHA-256 initialization failed");
    DWORD objectSize{}, resultSize{};
    BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize), sizeof(objectSize), &resultSize, 0);
    std::vector<unsigned char> object(objectSize), digest(32);
    if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) < 0) { BCryptCloseAlgorithmProvider(algorithm, 0); throw std::runtime_error("SHA-256 initialization failed"); }
    std::ifstream file(path, std::ios::binary); std::vector<char> buffer(128 * 1024);
    while (file) { file.read(buffer.data(), buffer.size()); const auto count = file.gcount(); if (count > 0) BCryptHashData(hash, reinterpret_cast<PUCHAR>(buffer.data()), static_cast<ULONG>(count), 0); }
    BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0); BCryptDestroyHash(hash); BCryptCloseAlgorithmProvider(algorithm, 0);
    static constexpr wchar_t hex[] = L"0123456789ABCDEF"; std::wstring result; result.reserve(64);
    for (auto byte : digest) { result += hex[byte >> 4]; result += hex[byte & 15]; }
    return result;
}

size_t ZipWriteCallback(void* opaque, mz_uint64 offset, const void* buffer, size_t size) {
    auto* context = static_cast<ExtractContext*>(opaque);
    LARGE_INTEGER position{}; position.QuadPart = static_cast<LONGLONG>(offset);
    if (!SetFilePointerEx(context->file, position, nullptr, FILE_BEGIN)) return 0;
    DWORD written{};
    return WriteFile(context->file, buffer, static_cast<DWORD>(size), &written, nullptr) ? written : 0;
}

std::set<std::wstring> ExtractDirectly(const fs::path& packagePath, const fs::path& installDirectory, std::vector<PendingFile>& pending) {
    FILE* archiveFile{}; _wfopen_s(&archiveFile, packagePath.c_str(), L"rb");
    if (!archiveFile) throw std::runtime_error("Unable to open update ZIP");
    _fseeki64(archiveFile, 0, SEEK_END); const auto archiveSize = _ftelli64(archiveFile); _fseeki64(archiveFile, 0, SEEK_SET);
    mz_zip_archive archive{};
    if (!mz_zip_reader_init_cfile(&archive, archiveFile, static_cast<mz_uint64>(archiveSize), 0)) { fclose(archiveFile); throw std::runtime_error("Invalid update ZIP"); }

    std::set<std::wstring> packageFiles;
    const mz_uint count = mz_zip_reader_get_num_files(&archive);
    for (mz_uint index = 0; index < count; ++index) {
        mz_zip_archive_file_stat stat{};
        if (!mz_zip_reader_file_stat(&archive, index, &stat)) { mz_zip_reader_end(&archive); fclose(archiveFile); throw std::runtime_error("Unable to read ZIP entry"); }
        if (stat.m_is_directory) continue;
        std::wstring relative = Utf8ToWide(stat.m_filename);
        std::replace(relative.begin(), relative.end(), L'/', L'\\');
        if (_wcsicmp(relative.c_str(), kManagedFilesName) == 0) continue;
        const fs::path destination = DestinationPath(installDirectory, relative);
        fs::create_directories(destination.parent_path());
        fs::path temporary = destination; temporary += L".update-new";
        DeleteFileW(temporary.c_str());
        HANDLE output = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (output == INVALID_HANDLE_VALUE) { mz_zip_reader_end(&archive); fclose(archiveFile); throw std::runtime_error("Unable to create temporary update file"); }
        ExtractContext context{output};
        const bool extracted = mz_zip_reader_extract_to_callback(&archive, index, ZipWriteCallback, &context, 0) == MZ_TRUE;
        FlushFileBuffers(output); CloseHandle(output);
        if (!extracted) { DeleteFileW(temporary.c_str()); mz_zip_reader_end(&archive); fclose(archiveFile); throw std::runtime_error("Unable to extract update file"); }
        pending.push_back({relative, temporary, destination}); packageFiles.insert(relative);
        PostProgress(static_cast<int>((index + 1) * 100 / std::max<mz_uint>(count, 1)));
    }
    mz_zip_reader_end(&archive); fclose(archiveFile);
    return packageFiles;
}

void ReplacePendingFiles(const std::vector<PendingFile>& pending) {
    for (const auto& file : pending) {
        bool replaced{};
        for (int attempt = 0; attempt < 20 && !replaced; ++attempt) {
            replaced = MoveFileExW(file.temporaryPath.c_str(), file.destinationPath.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
            if (!replaced) Sleep(250);
        }
        if (!replaced) throw std::runtime_error("Unable to replace an application file");
    }
}

std::set<std::wstring> ReadManagedFiles(const fs::path& path) {
    std::set<std::wstring> result; std::ifstream file(path, std::ios::binary);
    std::string json((std::istreambuf_iterator<char>(file)), {}); bool inString{}; bool escaped{}; std::string value;
    for (char ch : json) {
        if (!inString) { if (ch == '"') { inString = true; value.clear(); } continue; }
        if (escaped) { value += ch == 'n' ? '\n' : ch == 'r' ? '\r' : ch == 't' ? '\t' : ch; escaped = false; continue; }
        if (ch == '\\') { escaped = true; continue; }
        if (ch == '"') { inString = false; try { result.insert(Utf8ToWide(value.c_str())); } catch (...) {} continue; }
        value += ch;
    }
    return result;
}

void WriteManagedFiles(const fs::path& path, const std::set<std::wstring>& files) {
    std::ofstream output(path, std::ios::binary | std::ios::trunc); output << "[\n"; size_t index{};
    for (const auto& file : files) { std::string utf8 = WideToUtf8(file); std::string escaped; for (char ch : utf8) { if (ch == '\\' || ch == '"') escaped += '\\'; escaped += ch; } output << "  \"" << escaped << "\"" << (++index < files.size() ? "," : "") << "\n"; }
    output << "]\n";
}

void DeleteObsoleteFiles(const fs::path& installDirectory, const std::set<std::wstring>& currentFiles) {
    const fs::path manifest = installDirectory / kManagedFilesName;
    for (const auto& previous : ReadManagedFiles(manifest)) {
        if (currentFiles.find(previous) == currentFiles.end()) { const auto path = DestinationPath(installDirectory, previous); DeleteFileW(path.c_str()); }
    }
}

void WriteErrorLog(const std::wstring& message) {
    try { std::wofstream log(g_options.installDirectory / L"update-error.log", std::ios::trunc); log << message; } catch (...) {}
}

DWORD WINAPI UpdateWorker(void*) {
    try {
        PostStatus(L"앱이 종료되기를 기다리고 있습니다..."); WaitForProcessExit(g_options.processId);
        const fs::path workingDirectory = g_options.acceptedFile.parent_path();
        const fs::path packagePath = workingDirectory / L"update.zip";
        PostStatus(L"업데이트를 다운로드하고 있습니다..."); PostProgress(0); DownloadFile(g_options.packageUrl, packagePath);
        PostStatus(L"다운로드한 파일을 확인하고 있습니다...");
        if (_wcsicmp(Sha256(packagePath).c_str(), g_options.sha256.c_str()) != 0) throw std::runtime_error("SHA-256 mismatch");
        PostStatus(L"새 버전 파일을 준비하고 있습니다..."); PostProgress(0);
        std::vector<PendingFile> pending; const auto packageFiles = ExtractDirectly(packagePath, g_options.installDirectory, pending);
        if (packageFiles.find(g_options.executable.wstring()) == packageFiles.end()) throw std::runtime_error("Application executable is missing from the update package");
        PostStatus(L"새 버전을 설치하고 있습니다..."); ReplacePendingFiles(pending); DeleteObsoleteFiles(g_options.installDirectory, packageFiles);
        WriteManagedFiles(g_options.installDirectory / kManagedFilesName, packageFiles); DeleteFileW(packagePath.c_str());
        PostStatus(L"업데이트 완료");
        const fs::path executable = DestinationPath(g_options.installDirectory, g_options.executable.wstring());
        ShellExecuteW(nullptr, L"open", executable.c_str(), nullptr, g_options.installDirectory.c_str(), SW_SHOWNORMAL);
        PostMessageW(g_window, kDoneMessage, 0, 0);
    } catch (const std::exception& error) {
        const std::wstring message = Utf8ToWide(error.what()); WriteErrorLog(message); PostMessageW(g_window, kErrorMessage, 0, reinterpret_cast<LPARAM>(new std::wstring(message)));
    } catch (...) {
        const std::wstring message = L"알 수 없는 업데이트 오류가 발생했습니다."; WriteErrorLog(message); PostMessageW(g_window, kErrorMessage, 0, reinterpret_cast<LPARAM>(new std::wstring(message)));
    }
    return 0;
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
    case WM_CREATE: {
        constexpr int horizontalPadding = 26;
        RECT client{};
        GetClientRect(window, &client);
        const int contentWidth = (std::max)(1L, client.right - client.left - horizontalPadding * 2);
        g_status = CreateWindowW(L"STATIC", L"업데이트를 준비하고 있습니다...", WS_CHILD | WS_VISIBLE,
            horizontalPadding, 30, contentWidth, 28, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kStatusControl)), nullptr, nullptr);
        g_progress = CreateWindowExW(0, PROGRESS_CLASSW, nullptr, WS_CHILD | WS_VISIBLE,
            horizontalPadding, 78, contentWidth, 18, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kProgressControl)), nullptr, nullptr);
        const int fontHeight = -MulDiv(9, GetDpiForWindow(window), 72);
        g_statusFont = CreateFontW(fontHeight, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
            OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"굴림");
        if (g_statusFont) SendMessageW(g_status, WM_SETFONT, reinterpret_cast<WPARAM>(g_statusFont), TRUE);
        SendMessageW(g_progress, PBM_SETRANGE, 0, MAKELPARAM(0, 100));
        return 0;
    }
    case WM_CTLCOLORSTATIC:
        SetTextColor(reinterpret_cast<HDC>(wParam), RGB(230, 232, 238));
        SetBkColor(reinterpret_cast<HDC>(wParam), RGB(35, 37, 43));
        SetBkMode(reinterpret_cast<HDC>(wParam), OPAQUE);
        return GetClassLongPtrW(window, GCLP_HBRBACKGROUND);
    case kStatusMessage: {
        std::unique_ptr<std::wstring> text(reinterpret_cast<std::wstring*>(lParam));
        SetWindowTextW(g_status, text->c_str());
        RedrawWindow(g_status, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
        return 0;
    }
    case kProgressMessage: SendMessageW(g_progress, PBM_SETPOS, wParam, 0); return 0;
    case kErrorMessage: { std::unique_ptr<std::wstring> text(reinterpret_cast<std::wstring*>(lParam)); MessageBoxW(window, (L"업데이트에 실패했습니다.\n\n" + *text).c_str(), kWindowTitle, MB_OK | MB_ICONERROR); g_allowClose = true; DestroyWindow(window); return 0; }
    case kDoneMessage: g_allowClose = true; DestroyWindow(window); return 0;
    case WM_CLOSE: if (g_allowClose) DestroyWindow(window); return 0;
    case WM_DESTROY:
        if (g_statusFont) { DeleteObject(g_statusFont); g_statusFont = nullptr; }
        PostQuitMessage(0);
        return 0;
    default: return DefWindowProcW(window, message, wParam, lParam);
    }
}

bool ParseOptions(const std::vector<std::wstring>& args, Options& options) {
    const auto pid = GetArgument(args, L"--pid");
    options.installDirectory = GetArgument(args, L"--install-dir"); options.packageUrl = GetArgument(args, L"--package-url");
    options.sha256 = GetArgument(args, L"--sha256"); options.executable = GetArgument(args, L"--executable");
    options.currentVersion = GetArgument(args, L"--current-version"); options.latestVersion = GetArgument(args, L"--latest-version");
    options.acceptedFile = GetArgument(args, L"--accepted-file"); options.skipConfirmation = HasArgument(args, L"--skip-confirmation");
    try { options.processId = static_cast<DWORD>(std::stoul(pid)); } catch (...) { return false; }
    return options.processId && !options.installDirectory.empty() && !options.packageUrl.empty() && !options.sha256.empty() && !options.executable.empty() && !options.currentVersion.empty() && !options.latestVersion.empty() && !options.acceptedFile.empty();
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int showCommand) {
    int count{};
    LPWSTR* rawArgs = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!rawArgs) return 1;
    std::vector<std::wstring> args;
    for (int i = 0; i < count; ++i) args.emplace_back(rawArgs[i]);
    LocalFree(rawArgs);
    if (!ParseOptions(args, g_options)) { MessageBoxW(nullptr, L"단독으로 실행할 수 없습니다.", kWindowTitle, MB_OK | MB_ICONINFORMATION); return 0; }
    if (!g_options.skipConfirmation) {
        const std::wstring message = L"새로운 버전이 발견되었습니다.\n\n현재 버전: " + g_options.currentVersion + L"\n최신 버전: " + g_options.latestVersion + L"\n\n업데이트를 진행할까요?";
        if (MessageBoxW(nullptr, message.c_str(), kWindowTitle, MB_YESNO | MB_ICONINFORMATION) != IDYES) return 0;
    }

    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_PROGRESS_CLASS}; InitCommonControlsEx(&controls);
    WNDCLASSEXW windowClass{sizeof(windowClass)}; windowClass.lpfnWndProc = WindowProc; windowClass.hInstance = instance; windowClass.hCursor = LoadCursor(nullptr, IDC_ARROW); windowClass.hbrBackground = CreateSolidBrush(RGB(35, 37, 43)); windowClass.lpszClassName = kWindowClass;
    RegisterClassExW(&windowClass);
    g_window = CreateWindowExW(WS_EX_DLGMODALFRAME, kWindowClass, kWindowTitle, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        CW_USEDEFAULT, CW_USEDEFAULT, 430, 150, nullptr, nullptr, instance, nullptr);
    if (!g_window) return 1;
    RECT rect{}; GetWindowRect(g_window, &rect); SetWindowPos(g_window, nullptr, (GetSystemMetrics(SM_CXSCREEN) - (rect.right - rect.left)) / 2,
        (GetSystemMetrics(SM_CYSCREEN) - (rect.bottom - rect.top)) / 2, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    ShowWindow(g_window, showCommand); UpdateWindow(g_window);
    try { WriteAcceptedFlag(); } catch (...) { MessageBoxW(g_window, L"업데이트를 시작하지 못했습니다.", kWindowTitle, MB_OK | MB_ICONERROR); return 1; }
    HANDLE worker = CreateThread(nullptr, 0, UpdateWorker, nullptr, 0, nullptr); if (worker) CloseHandle(worker);
    MSG message{}; while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    return 0;
}
